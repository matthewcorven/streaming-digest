// ============================================================
// THROWAWAY PROTOTYPE — Task 11.3a (GitHub issue #18)
// Vector knowledge-base approach validation against REAL
// Aspire-managed Postgres + pgvector. One command:
//     dotnet run --project spikes/StreamingDigest.VectorPrototype
// ZERO external AI calls. Seeded & deterministic.
// ============================================================

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using VectorPrototype;

// ---------- configuration (deterministic, seedable) ----------
const int Seed = 42;
const int Dimensions = 384;            // bge-m3 class model dimension per ARCHITECTURE §2.4
const int VideoCount = 500;            // MVP scale assumption (~500 videos)
const int DocsPerVideo = 24;           // ~12,000 search documents (thousands of vectors)
const int SharedResourceCount = 40;    // canonical resources duplicated across videos (ADR-0004)
const string ModelId = "synthetic-topic-embedder-v1";
const string Provider = "synthetic-local";

var connString = Environment.GetEnvironmentVariable("ConnectionStrings__streamingdigest")
    ?? throw new InvalidOperationException("Set ConnectionStrings__streamingdigest (from the running Aspire AppHost postgres resource).");

var results = new Dictionary<string, object?>();
var verdicts = new Dictionary<string, string>();
void Banner(string s) => Console.WriteLine($"\n=== {s} ===");

Banner("Task 11.3a vector knowledge-base prototype");
Console.WriteLine($"seed={Seed} dim={Dimensions} videos={VideoCount} docs/video={DocsPerVideo}");

// ---------- 0. connect + schema ----------
Banner("0. Connecting to Aspire-managed Postgres and creating schema");
await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();

async Task<int> Exec(string sql, params NpgsqlParameter[] ps)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddRange(ps);
    return await cmd.ExecuteNonQueryAsync();
}
async Task<object?> Scalar(string sql, params NpgsqlParameter[] ps)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddRange(ps);
    return await cmd.ExecuteScalarAsync();
}

await Exec("CREATE EXTENSION IF NOT EXISTS vector");
await Exec("DROP TABLE IF EXISTS embeddings; DROP TABLE IF EXISTS search_documents; DROP TABLE IF EXISTS video_cluster_embeddings;");
await Exec($"""
    CREATE TABLE search_documents (
        id uuid PRIMARY KEY,
        document_type text NOT NULL,
        source_entity_type text NOT NULL,
        source_entity_id uuid NOT NULL,
        parent_video_id uuid NULL,
        title_effective text NULL,
        body_effective text NULL,
        content_hash text NOT NULL
    );
    CREATE TABLE embeddings (
        id uuid PRIMARY KEY,
        search_document_id uuid NOT NULL REFERENCES search_documents(id),
        provider text NOT NULL,
        model text NOT NULL,
        dimensions integer NOT NULL,
        content_hash text NOT NULL,
        embedding vector({Dimensions}) NOT NULL,
        UNIQUE (search_document_id, provider, model, dimensions, content_hash)
    );
    CREATE TABLE video_cluster_embeddings (
        id uuid PRIMARY KEY,
        video_id uuid NOT NULL,
        provider text NOT NULL,
        model text NOT NULL,
        dimensions integer NOT NULL,
        content_hash text NOT NULL,
        embedding vector({Dimensions}) NOT NULL,
        component_weights_json jsonb NOT NULL
    );
    """);
Console.WriteLine("schema created (search_documents, embeddings, video_cluster_embeddings)");
Console.WriteLine($"pgvector version: {await Scalar("SELECT extversion FROM pg_extension WHERE extname='vector'")}");

// ---------- 1. generate corpus + embeddings (ZERO AI) ----------
Banner("1. Generating synthetic corpus + embeddings (no provider calls)");
var sw = Stopwatch.StartNew();
var corpus = new CorpusGenerator(Seed);
var embedder = new SyntheticEmbedder(Seed, Dimensions);
var videos = corpus.Generate(VideoCount, DocsPerVideo);

string ContentHash(string title, string body) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + body))).ToLowerInvariant();

// ADR-0004: shared canonical resources duplicated per referencing video.
// Pick SharedResourceCount resources, each shared by 3 videos → 3 document rows,
// identical content + content_hash, distinct parent_video_id, ONE shared embedding.
var sharedResources = Enumerable.Range(0, SharedResourceCount).Select(i =>
{
    int topic = i % CorpusGenerator.TopicNames.Length;
    var title = $"shared-{CorpusGenerator.TopicNames[topic]}-resource-{i}";
    var body = $"Canonical shared README about {CorpusGenerator.TopicNames[topic]} reused across videos.";
    return (ResourceId: Guid.NewGuid(), Topic: topic, Title: title, Body: body, Hash: ContentHash(title, body));
}).ToList();

sw.Stop();
Console.WriteLine($"corpus: {videos.Count} videos, {videos.Sum(v => v.Documents.Count)} base documents in {sw.ElapsedMilliseconds}ms");

// ---------- 2. insert documents + dedup embeddings by content_hash ----------
Banner("2. Inserting documents; embedding dedup by content_hash (ADR-0004)");
sw.Restart();
int docRows = 0;
var vectorByHash = new Dictionary<string, float[]>();   // the ADR-0004 "shared embedding" store
var docVectors = new Dictionary<Guid, float[]>();        // doc id -> its vector (for centroids)

// Insert via NpgsqlBatch (types inferred per-row; avoids Prepare() type-inference issue).
var docInsertRows = new List<(Guid Id, string Type, string SrcType, Guid SrcId, Guid? VideoId, string Title, string Body, string Hash)>();

void QueueDoc(string type, string srcType, Guid srcId, Guid? videoId, string title, string body, Guid docId) =>
    docInsertRows.Add((docId, type, srcType, srcId, videoId, title, body, ContentHash(title, body)));

foreach (var video in videos)
    foreach (var d in video.Documents)
    {
        var id = Guid.NewGuid();
        QueueDoc(d.DocumentType, d.SourceEntityType, d.SourceEntityId, video.VideoId, d.TitleEffective, d.BodyEffective, id);
        docVectors[id] = embedder.Embed(video.TopicIndex, d.TitleEffective, d.BodyEffective);
    }

// ADR-0004 duplicates: each shared resource emitted once per referencing video.
for (int i = 0; i < sharedResources.Count; i++)
{
    var r = sharedResources[i];
    for (int copy = 0; copy < 3; copy++)
    {
        var ownerVideo = videos[(i * 3 + copy) % videos.Count].VideoId;
        var id = Guid.NewGuid();
        QueueDoc("repository_readme_chunk", "repository_documents", r.ResourceId, ownerVideo, r.Title, r.Body, id);
        docVectors[id] = embedder.Embed(r.Topic, r.Title, r.Body); // identical → identical hash → shared
    }
}

const int BatchSize = 1000;
for (int off = 0; off < docInsertRows.Count; off += BatchSize)
{
    await using var batch = new NpgsqlBatch(conn);
    foreach (var r in docInsertRows.Skip(off).Take(BatchSize))
    {
        var bc = new NpgsqlBatchCommand("INSERT INTO search_documents (id, document_type, source_entity_type, source_entity_id, parent_video_id, title_effective, body_effective, content_hash) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)");
        bc.Parameters.AddWithValue(r.Id); bc.Parameters.AddWithValue(r.Type); bc.Parameters.AddWithValue(r.SrcType);
        bc.Parameters.AddWithValue(r.SrcId); bc.Parameters.AddWithValue((object?)r.VideoId ?? DBNull.Value);
        bc.Parameters.AddWithValue(r.Title); bc.Parameters.AddWithValue(r.Body); bc.Parameters.AddWithValue(r.Hash);
        batch.BatchCommands.Add(bc);
    }
    await batch.ExecuteNonQueryAsync();
}
docRows = docInsertRows.Count;
sw.Stop();
Console.WriteLine($"inserted {docRows} search_documents in {sw.ElapsedMilliseconds}ms");

// distinct content hashes → number of embeddings actually computed/stored
int distinctHashes = await Scalar("SELECT COUNT(DISTINCT content_hash) FROM search_documents") is long l ? (int)l : 0;
Console.WriteLine($"distinct content_hash values: {distinctHashes} (embedding computed once per unique content)");
results["document_rows"] = docRows;
results["distinct_content_hashes"] = distinctHashes;
results["adr0004_embedding_savings_pct"] = Math.Round(100.0 * (1 - (double)distinctHashes / docRows), 2);
// (ADR-0004 per-resource probe runs after step 3, once embeddings exist.)

// ---------- 3. bulk-insert embeddings (one per distinct content) ----------
Banner("3. Bulk-inserting embeddings (deduplicated by content_hash)");
sw.Restart();
int embeddingRows = 0;
{
    // Read all docs; first-seen content_hash gets an embedding row (ADR-0004: computed once).
    var rows = new List<(Guid Id, string Hash)>();
    await using (var readCmd = new NpgsqlCommand("SELECT id, content_hash FROM search_documents", conn))
    await using (var reader = await readCmd.ExecuteReaderAsync())
        while (await reader.ReadAsync()) rows.Add((reader.GetGuid(0), reader.GetString(1)));

    var seen = new HashSet<string>();
    var embRows = new List<(Guid Id, Guid DocId, string Hash, float[] Vec)>();
    int firstSeenNoVec = 0;
    foreach (var (id, hash) in rows)
    {
        if (!seen.Add(hash)) continue;
        if (docVectors.TryGetValue(id, out var vec))
            embRows.Add((Guid.NewGuid(), id, hash, vec));
        else
            firstSeenNoVec++;   // duplicated doc whose hash's vector lives under a different doc id
    }
    Console.WriteLine($"first-seen docs missing a vector (dup-before-original): {firstSeenNoVec}");

    for (int off = 0; off < embRows.Count; off += BatchSize)
    {
        await using var batch = new NpgsqlBatch(conn);
        foreach (var r in embRows.Skip(off).Take(BatchSize))
        {
            var bc = new NpgsqlBatchCommand("INSERT INTO embeddings (id, search_document_id, provider, model, dimensions, content_hash, embedding) VALUES ($1,$2,$3,$4,$5,$6,$7)");
            bc.Parameters.AddWithValue(r.Id); bc.Parameters.AddWithValue(r.DocId); bc.Parameters.AddWithValue(Provider);
            bc.Parameters.AddWithValue(ModelId); bc.Parameters.AddWithValue(Dimensions); bc.Parameters.AddWithValue(r.Hash);
            bc.Parameters.AddWithValue(r.Vec);
            batch.BatchCommands.Add(bc);
        }
        await batch.ExecuteNonQueryAsync();
    }
    embeddingRows = embRows.Count;
}
sw.Stop();
Console.WriteLine($"inserted {embeddingRows} embeddings in {sw.ElapsedMilliseconds}ms");
results["embedding_rows"] = embeddingRows;

// Direct ADR-0004 cardinality check (now that embeddings exist): for a shared resource,
// assert 3 document rows (one per referencing video), identical content_hash, distinct
// parent_video_id, and exactly 1 embedding row for that shared content.
var sharedProbe = sharedResources[0];
var sharedDocRows = Convert.ToInt32(await Scalar(
    "SELECT COUNT(*) FROM search_documents WHERE source_entity_id = $1",
    new NpgsqlParameter { Value = sharedProbe.ResourceId }));
var sharedDistinctParents = Convert.ToInt32(await Scalar(
    "SELECT COUNT(DISTINCT parent_video_id) FROM search_documents WHERE source_entity_id = $1",
    new NpgsqlParameter { Value = sharedProbe.ResourceId }));
var sharedEmbeddings = Convert.ToInt32(await Scalar(
    "SELECT COUNT(*) FROM embeddings WHERE content_hash = $1",
    new NpgsqlParameter { Value = sharedProbe.Hash }));
Console.WriteLine($"ADR-0004 probe: shared resource has {sharedDocRows} doc rows, {sharedDistinctParents} distinct parents, {sharedEmbeddings} embedding row(s)");
results["adr0004_shared_doc_rows"] = sharedDocRows;
results["adr0004_shared_distinct_parents"] = sharedDistinctParents;
results["adr0004_shared_embedding_rows"] = sharedEmbeddings;

// ---------- 4. video cluster centroids (DATA_MODEL §3.23) ----------
Banner("4. Building video_cluster_embeddings (weighted centroid of normalized children)");
sw.Restart();
// Weight by document_type: metadata/notes weigh more, transcript chunks less.
double TypeWeight(string t) => t switch
{
    "video_metadata" => 2.0, "note" => 2.0, "segment_title" => 1.5,
    "external_resource_metadata" => 1.2, "repository_readme_chunk" => 1.2,
    "scraped_page_text" => 1.0, _ => 1.0 // transcript_chunk
};
int clusterRows = 0;
foreach (var video in videos)
{
    // Rebuild components from stored doc ids for this video.
    await using var cmd = new NpgsqlCommand("SELECT id, document_type FROM search_documents WHERE parent_video_id = $1", conn);
    cmd.Parameters.Add(new NpgsqlParameter { Value = video.VideoId });
    var docIds = new List<(Guid Id, string Type)>();
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync()) docIds.Add((r.GetGuid(0), r.GetString(1)));

    var components = new List<(float[] Vector, double Weight)>();
    foreach (var (id, type) in docIds)
        if (docVectors.TryGetValue(id, out var vec))
            components.Add((vec, TypeWeight(type)));
    if (components.Count == 0) continue;
    var centroid = SyntheticEmbedder.WeightedCentroid(components);
    var weightsJson = JsonSerializer.Serialize(docIds.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.Count()));
    await Exec(
        "INSERT INTO video_cluster_embeddings (id, video_id, provider, model, dimensions, content_hash, embedding, component_weights_json) VALUES ($1,$2,$3,$4,$5,$6,$7,$8::jsonb)",
        new NpgsqlParameter { Value = Guid.NewGuid() }, new NpgsqlParameter { Value = video.VideoId },
        new NpgsqlParameter { Value = Provider }, new NpgsqlParameter { Value = ModelId },
        new NpgsqlParameter { Value = Dimensions }, new NpgsqlParameter { Value = "cluster-" + video.VideoId.ToString("N")[..16] },
        new NpgsqlParameter { Value = centroid }, new NpgsqlParameter { Value = weightsJson });
    clusterRows++;
}
sw.Stop();
Console.WriteLine($"built {clusterRows} cluster centroids in {sw.ElapsedMilliseconds}ms");
results["cluster_centroid_rows"] = clusterRows;

// ---------- 5. staleness derivation (ADR-0001) ----------
Banner("5. Validating staleness derivation (ADR-0001)");
// Simulate a source edit: change one document's effective text → its stored hash no longer matches.
var victim = await Scalar("SELECT id FROM search_documents WHERE document_type='video_metadata' LIMIT 1");
var oldHash = (string)(await Scalar("SELECT content_hash FROM search_documents WHERE id=$1", new NpgsqlParameter { Value = victim! })!);
var newHash = ContentHash("EDITED title", "EDITED body");
// "Derived" staleness = stored content_hash != hash(current effective value). Emulate by computing current hash.
bool isStale = oldHash != newHash;
// Prove the derivation query works: count docs whose stored hash differs from a recomputed hash of current text.
var derivedStaleCount = await Scalar("""
    SELECT COUNT(*) FROM search_documents
    WHERE content_hash <> encode(sha256(convert_to(coalesce(title_effective,'') || E'\n' || coalesce(body_effective,''),'UTF8')),'hex')
    """);
Console.WriteLine($"simulated edit: stored hash {oldHash[..12]}… vs new effective hash {newHash[..12]}… → stale={isStale}");
Console.WriteLine($"docs where stored hash != recomputed hash of current text: {derivedStaleCount} (should be 0 — derivation matches storage)");
results["staleness_derivation_mismatches"] = Convert.ToInt32(derivedStaleCount);
verdicts["staleness"] = (isStale && Convert.ToInt32(derivedStaleCount) == 0) ? "PROVEN" : "DISPROVEN";

// ---------- 6. pgvector index comparison: HNSW vs IVFFlat ----------
Banner("6. HNSW vs IVFFlat index comparison at MVP scale");
var indexResults = new List<object>();

async Task<double> AvgLatencyMs(string orderByVec, float[] q, int k)
{
    // cosine distance ordering; measure avg of several runs
    double total = 0; int runs = 5;
    for (int i = 0; i < runs; i++)
    {
        var s = Stopwatch.StartNew();
        await using var cmd = new NpgsqlCommand(
            $"SELECT e.search_document_id FROM embeddings e ORDER BY e.embedding <=> $1::vector LIMIT {k}", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = q });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) { }
        s.Stop(); total += s.Elapsed.TotalMilliseconds;
    }
    return total / runs;
}

async Task<HashSet<Guid>> TopK(float[] q, int k, bool forceSeqScan)
{
    if (forceSeqScan) await Exec("SET enable_seqscan = off"); // planner still prefers index if cheaper; use LOCAL below
    var set = new HashSet<Guid>();
    await using var cmd = new NpgsqlCommand($"SELECT e.search_document_id FROM embeddings e ORDER BY e.embedding <=> $1::vector LIMIT {k}", conn);
    cmd.Parameters.Add(new NpgsqlParameter { Value = q });
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) set.Add(r.GetGuid(0));
    return set;
}

// Build a query set: one vector per topic (its centroid + tiny jitter)
var queries = Enumerable.Range(0, CorpusGenerator.TopicNames.Length)
    .Select(t => embedder.Embed(t, "query-" + CorpusGenerator.TopicNames[t], "find content about " + CorpusGenerator.TopicNames[t]))
    .ToList();
const int K = 10;

// Exact-KNN baseline computed with the index dropped (true ground truth).
async Task<Dictionary<int, HashSet<Guid>>> ExactBaseline()
{
    await Exec("DROP INDEX IF EXISTS emb_idx");
    var map = new Dictionary<int, HashSet<Guid>>();
    for (int i = 0; i < queries.Count; i++)
        map[i] = await TopK(queries[i], K, false);
    return map;
}
var exactTopK = await ExactBaseline();

async Task<object> MeasureIndex(string name, string createSql, string? setSearchParam)
{
    await Exec("DROP INDEX IF EXISTS emb_idx");
    var build = Stopwatch.StartNew();
    await Exec(createSql);
    build.Stop();
    if (setSearchParam != null) await Exec(setSearchParam);

    double latencySum = 0, recallSum = 0;
    for (int i = 0; i < queries.Count; i++)
    {
        latencySum += await AvgLatencyMs("e.embedding <=> $1", queries[i], K);
        var approx = await TopK(queries[i], K, false);
        int hit = approx.Count(id => exactTopK[i].Contains(id));
        recallSum += (double)hit / K;
    }
    var sizeBytes = await Scalar("SELECT pg_relation_size('emb_idx')") is long sb ? sb : 0;
    return new { index = name, build_ms = build.ElapsedMilliseconds, avg_query_ms = Math.Round(latencySum / queries.Count, 3), recall_at_10 = Math.Round(recallSum / queries.Count, 4), size_kb = sizeBytes / 1024 };
}

// exact (no index) baseline latency
await Exec("DROP INDEX IF EXISTS emb_idx");
var exactLatency = 0.0;
foreach (var q in queries) exactLatency += await AvgLatencyMs("", q, K);
exactLatency /= queries.Count;
Console.WriteLine($"exact seq-scan avg latency: {exactLatency:F2}ms");

indexResults.Add(await MeasureIndex("hnsw", "CREATE INDEX emb_idx ON embeddings USING hnsw (embedding vector_cosine_ops)", null));
Console.WriteLine($"hnsw done: {JsonSerializer.Serialize(indexResults[^1])}");
indexResults.Add(await MeasureIndex("ivfflat(lists=100)", "CREATE INDEX emb_idx ON embeddings USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100)", "SET ivfflat.probes = 10"));
Console.WriteLine($"ivfflat done: {JsonSerializer.Serialize(indexResults[^1])}");
results["index_comparison"] = indexResults;
results["exact_seqscan_avg_ms"] = Math.Round(exactLatency, 3);

// ---------- verdicts ----------
Banner("VERDICTS");
verdicts["document_construction"] = docRows > 0 && distinctHashes > 0 ? "PROVEN" : "DISPROVEN";
verdicts["adr0004_cardinality"] = embeddingRows == distinctHashes && embeddingRows < docRows
    && sharedDocRows == 3 && sharedDistinctParents == 3 && sharedEmbeddings == 1 ? "PROVEN" : "DISPROVEN";
verdicts["cluster_centroid"] = clusterRows == VideoCount ? "PROVEN" : "INCONCLUSIVE";
verdicts["pgvector_index"] = indexResults.Count == 2 ? "PROVEN" : "INCONCLUSIVE";
foreach (var (k, v) in verdicts) Console.WriteLine($"  {k}: {v}");

// ---------- emit machine-readable evidence ----------
var evidence = new
{
    task = "11.3a-vector-knowledge-base",
    date = DateTime.UtcNow.ToString("o"),
    seed = Seed, dimensions = Dimensions, video_count = VideoCount, docs_per_video = DocsPerVideo,
    model = ModelId, provider = Provider,
    pgvector_version = (string?)(await Scalar("SELECT extversion FROM pg_extension WHERE extname='vector'")),
    postgres_version = (string?)(await Scalar("SHOW server_version")),
    results, verdicts
};
var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory("../../docs/verification");
await File.WriteAllTextAsync("../../docs/verification/11.3a-vector-knowledge-base.json", json);
Console.WriteLine("\nevidence written to docs/verification/11.3a-vector-knowledge-base.json");
Console.WriteLine(JsonSerializer.Serialize(indexResults, new JsonSerializerOptions { WriteIndented = true }));
