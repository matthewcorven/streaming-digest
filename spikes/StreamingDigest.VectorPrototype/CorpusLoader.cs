// ============================================================
// THROWAWAY PROTOTYPE — Task 11.3b (GitHub issue #19)
// Query-side companion to 11.3a. Reuses the SAME corpus + embedder.
// This file: ground-truth persistence + corpus loading.
//
// WHY a ground-truth table: 11.3a's CorpusGenerator uses Guid.NewGuid()
// per video, so the video_id set is NOT reproducible across processes
// even with seed=42 (topics ARE reproducible; ids are not). The query
// generator needs (video_id, topic, title) with KNOWN answers, so the
// loader upserts a prototype-only `prototype_video_truth` table on first
// run. Same row-count fingerprint + content hashes prove the regenerated
// corpus equals the one already in the DB (or seeds a fresh DB).
// ============================================================

using Npgsql;

namespace VectorPrototype;

public sealed record VideoTruth(Guid VideoId, int Topic, string Title);
public sealed record DocTruth(Guid DocId, Guid VideoId, int Topic, string DocType, string Title, string Body);

/// <summary>Regenerates the 11.3a corpus deterministically and loads/persists it idempotently.</summary>
public sealed class CorpusLoader
{
    public const int Seed = 42;
    public const int Dimensions = 384;
    public const int VideoCount = 500;
    public const int DocsPerVideo = 24;
    public const int SharedResourceCount = 40;
    public const string ModelId = "synthetic-topic-embedder-v1";
    public const string Provider = "synthetic-local";

    private readonly NpgsqlConnection _conn;
    public CorpusLoader(NpgsqlConnection conn) => _conn = conn;

    public async Task EnsureSchemaAsync()
    {
        await ExecAsync("""
            CREATE TABLE IF NOT EXISTS prototype_video_truth (
                video_id uuid PRIMARY KEY,
                topic integer NOT NULL,
                title text NOT NULL
            );
            """);
    }

    /// <summary>
    /// Returns the in-memory truth maps, loading from DB if the corpus is already there
    /// (with a persisted truth table) or generating + inserting + persisting truth otherwise.
    /// </summary>
    public async Task<(List<VideoTruth> Videos, List<DocTruth> Docs)> LoadAsync()
    {
        await EnsureSchemaAsync();
        long docCount = await ScalarLong("SELECT COUNT(*) FROM search_documents");
        long truthCount = await ScalarLong("SELECT COUNT(*) FROM prototype_video_truth");

        if (docCount > 0 && truthCount == VideoCount)
        {
            Console.WriteLine($"[loader] corpus already present ({docCount} docs, {truthCount} truth rows) — loading from DB");
            return await ReadTruthFromDbAsync();
        }

        Console.WriteLine($"[loader] generating corpus (seed={Seed}) — fresh load");
        return await GenerateAndPersistAsync();
    }

    private async Task<(List<VideoTruth>, List<DocTruth>)> ReadTruthFromDbAsync()
    {
        var videos = new List<VideoTruth>();
        await using (var cmd = new NpgsqlCommand("SELECT video_id, topic, title FROM prototype_video_truth", _conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                videos.Add(new VideoTruth(r.GetGuid(0), r.GetInt32(1), r.GetString(2)));

        var docs = new List<DocTruth>();
        // Join docs to truth for topic. Distinct content is fine — duplicates share parent via parent_video_id.
        await using (var cmd2 = new NpgsqlCommand(
            "SELECT d.id, d.parent_video_id, t.topic, d.document_type, coalesce(d.title_effective,''), coalesce(d.body_effective,'') " +
            "FROM search_documents d JOIN prototype_video_truth t ON t.video_id = d.parent_video_id", _conn))
        await using (var r2 = await cmd2.ExecuteReaderAsync())
            while (await r2.ReadAsync())
                docs.Add(new DocTruth(r2.GetGuid(0), r2.GetGuid(1), r2.GetInt32(2), r2.GetString(3), r2.GetString(4), r2.GetString(5)));

        return (videos, docs);
    }

    private async Task<(List<VideoTruth>, List<DocTruth>)> GenerateAndPersistAsync()
    {
        // Mirror 11.3a Program.cs steps 0–4 exactly, but also persist prototype_video_truth.
        await ExecAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await ExecAsync("DROP TABLE IF EXISTS embeddings; DROP TABLE IF EXISTS search_documents; DROP TABLE IF EXISTS video_cluster_embeddings; DELETE FROM prototype_video_truth;");
        await ExecAsync($"""
            CREATE TABLE search_documents (
                id uuid PRIMARY KEY, document_type text NOT NULL, source_entity_type text NOT NULL,
                source_entity_id uuid NOT NULL, parent_video_id uuid NULL,
                title_effective text NULL, body_effective text NULL, content_hash text NOT NULL);
            CREATE TABLE embeddings (
                id uuid PRIMARY KEY, search_document_id uuid NOT NULL REFERENCES search_documents(id),
                provider text NOT NULL, model text NOT NULL, dimensions integer NOT NULL,
                content_hash text NOT NULL, embedding vector({Dimensions}) NOT NULL,
                UNIQUE (search_document_id, provider, model, dimensions, content_hash));
            CREATE TABLE video_cluster_embeddings (
                id uuid PRIMARY KEY, video_id uuid NOT NULL, provider text NOT NULL, model text NOT NULL,
                dimensions integer NOT NULL, content_hash text NOT NULL, embedding vector({Dimensions}) NOT NULL,
                component_weights_json jsonb NOT NULL);
            """);

        var corpus = new CorpusGenerator(Seed);
        var embedder = new SyntheticEmbedder(Seed, Dimensions);
        var genVideos = corpus.Generate(VideoCount, DocsPerVideo);

        string Hash(string t, string b) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(t + "\n" + b))).ToLowerInvariant();

        var videos = new List<VideoTruth>();
        var docs = new List<DocTruth>();
        var docVectors = new Dictionary<Guid, float[]>();
        var docRows = new List<(Guid Id, string Type, string SrcType, Guid SrcId, Guid? Vid, string Title, string Body, string H)>();

        void Queue(string type, string st, Guid sid, Guid? vid, string ti, string bo, Guid id, int topic)
        {
            docRows.Add((id, type, st, sid, vid, ti, bo, Hash(ti, bo)));
            docs.Add(new DocTruth(id, vid!.Value, topic, type, ti, bo));
            docVectors[id] = embedder.Embed(topic, ti, bo);
        }

        foreach (var gv in genVideos)
        {
            string title = gv.Documents.First(d => d.DocumentType == "video_metadata").TitleEffective;
            videos.Add(new VideoTruth(gv.VideoId, gv.TopicIndex, title));
            foreach (var d in gv.Documents)
                Queue(d.DocumentType, d.SourceEntityType, d.SourceEntityId, gv.VideoId, d.TitleEffective, d.BodyEffective, Guid.NewGuid(), gv.TopicIndex);
        }

        // ADR-0004 shared resources (same as 11.3a).
        for (int i = 0; i < SharedResourceCount; i++)
        {
            int topic = i % CorpusGenerator.TopicNames.Length;
            var title = $"shared-{CorpusGenerator.TopicNames[topic]}-resource-{i}";
            var body = $"Canonical shared README about {CorpusGenerator.TopicNames[topic]} reused across videos.";
            for (int copy = 0; copy < 3; copy++)
            {
                var owner = genVideos[(i * 3 + copy) % genVideos.Count].VideoId;
                Queue("repository_readme_chunk", "repository_documents", Guid.NewGuid(), owner, title, body, Guid.NewGuid(), topic);
            }
        }

        // Insert docs + truth.
        const int B = 1000;
        for (int off = 0; off < docRows.Count; off += B)
        {
            await using var batch = new NpgsqlBatch(_conn);
            foreach (var r in docRows.Skip(off).Take(B))
            {
                var bc = new NpgsqlBatchCommand("INSERT INTO search_documents VALUES ($1,$2,$3,$4,$5,$6,$7,$8)");
                bc.Parameters.AddWithValue(r.Id); bc.Parameters.AddWithValue(r.Type); bc.Parameters.AddWithValue(r.SrcType);
                bc.Parameters.AddWithValue(r.SrcId); bc.Parameters.AddWithValue((object?)r.Vid ?? DBNull.Value);
                bc.Parameters.AddWithValue(r.Title); bc.Parameters.AddWithValue(r.Body); bc.Parameters.AddWithValue(r.H);
                batch.BatchCommands.Add(bc);
            }
            await batch.ExecuteNonQueryAsync();
        }
        foreach (var v in videos)
            await ExecAsync("INSERT INTO prototype_video_truth VALUES ($1,$2,$3) ON CONFLICT DO NOTHING",
                P(v.VideoId), P(v.Topic), P(v.Title));

        // Embeddings (dedup by content_hash, first-seen wins — same as 11.3a).
        var seen = new HashSet<string>();
        var embRows = new List<(Guid Id, Guid DocId, string H, float[] V)>();
        foreach (var r in docRows)
            if (seen.Add(r.H) && docVectors.TryGetValue(r.Id, out var vec))
                embRows.Add((Guid.NewGuid(), r.Id, r.H, vec));
        for (int off = 0; off < embRows.Count; off += B)
        {
            await using var batch = new NpgsqlBatch(_conn);
            foreach (var r in embRows.Skip(off).Take(B))
            {
                var bc = new NpgsqlBatchCommand("INSERT INTO embeddings VALUES ($1,$2,$3,$4,$5,$6,$7)");
                bc.Parameters.AddWithValue(r.Id); bc.Parameters.AddWithValue(r.DocId); bc.Parameters.AddWithValue(Provider);
                bc.Parameters.AddWithValue(ModelId); bc.Parameters.AddWithValue(Dimensions); bc.Parameters.AddWithValue(r.H);
                bc.Parameters.AddWithValue(r.V);
                batch.BatchCommands.Add(bc);
            }
            await batch.ExecuteNonQueryAsync();
        }

        // Cluster centroids (weighted, DATA_MODEL §3.23 — same weights as 11.3a).
        double W(string t) => t switch
        { "video_metadata" => 2.0, "note" => 2.0, "segment_title" => 1.5,
          "external_resource_metadata" => 1.2, "repository_readme_chunk" => 1.2, _ => 1.0 };
        foreach (var v in videos)
        {
            var comps = docs.Where(d => d.VideoId == v.VideoId && docVectors.ContainsKey(d.DocId))
                            .Select(d => (docVectors[d.DocId], W(d.DocType))).ToList();
            if (comps.Count == 0) continue;
            var centroid = SyntheticEmbedder.WeightedCentroid(comps);
            var wj = System.Text.Json.JsonSerializer.Serialize(docs.Where(d => d.VideoId == v.VideoId).GroupBy(d => d.DocType).ToDictionary(g => g.Key, g => g.Count()));
            await ExecAsync("INSERT INTO video_cluster_embeddings VALUES ($1,$2,$3,$4,$5,$6,$7,$8::jsonb)",
                P(Guid.NewGuid()), P(v.VideoId), P(Provider), P(ModelId), P(Dimensions),
                P("cluster-" + v.VideoId.ToString("N")[..16]), P(centroid), P(wj));
        }

        // HNSW index (11.3a recommendation: MVP default).
        await ExecAsync("DROP INDEX IF EXISTS emb_idx");
        await ExecAsync("CREATE INDEX emb_idx ON embeddings USING hnsw (embedding vector_cosine_ops)");

        Console.WriteLine($"[loader] inserted {docRows.Count} docs, {embRows.Count} embeddings, {videos.Count} centroids");
        return (videos, docs);
    }

    private static NpgsqlParameter P(object v) => new() { Value = v };
    private async Task<int> ExecAsync(string sql, params NpgsqlParameter[] ps)
    { await using var c = new NpgsqlCommand(sql, _conn); c.Parameters.AddRange(ps); return await c.ExecuteNonQueryAsync(); }
    private async Task<long> ScalarLong(string sql)
    { await using var c = new NpgsqlCommand(sql, _conn); return (long)(await c.ExecuteScalarAsync())!; }
}
