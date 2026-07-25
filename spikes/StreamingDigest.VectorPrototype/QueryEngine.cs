// ============================================================
// THROWAWAY PROTOTYPE — Task 11.3b (GitHub issue #19)
// The query engine: hybrid scoring + cluster aggregation + relSimPct +
// ADR-0012 high-signal + related items, run against REAL pgvector.
//
// The TEXT side is REAL Postgres: to_tsvector(title weight A, body B)
// with ts_rank_cd, plus pg_trgm similarity for the vague/partial case.
// It is NOT simulated in memory — the whole question is how two real
// scoring systems combine.
//
// HONESTY: the synthetic query embedding jitters from the raw query text
// (see QueryGenerator header), so the vector side carries TOPIC signal
// only. Recall across query KINDS is therefore not a semantic-robustness
// measure. What this engine proves is the MECHANICS: formula math,
// normalization, aggregation, threshold plumbing, relSimPct, latency.
// ============================================================

using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using Pgvector;

namespace VectorPrototype;

public sealed class QueryEngine
{
    private readonly NpgsqlConnection _conn;
    private readonly SyntheticEmbedder _embedder;
    public QueryEngine(NpgsqlConnection conn, SyntheticEmbedder embedder)
    { _conn = conn; _embedder = embedder; }

    // DATA_MODEL §6 weights under test (base defaults).
    public sealed record Config(
        double TextWeight, double VectorWeight,
        double AggMax = 0.65, double AggTop3 = 0.25, double AggCoverage = 0.10,
        double NoteBoost = 0.08, double InteractionBoostCap = 0.05,
        int InteractionsPerVideo = 0,   // simulated recent-open count (0..5, cap at 5)
        int TopK = 10, int CandidatePool = 200, int PageSize = 20);

    public sealed record ScoredCluster(
        Guid VideoId, int Topic, string Title,
        double TextScore, double VectorScore, double DocScore,
        double ClusterBase, double CoverageScore, double NoteBoost, double InteractionBoost,
        double ClusterScore, double RelSimPct);

    public sealed record QueryResult(
        SyntheticQuery Query, List<ScoredCluster> Ranked, double LatencyMs,
        int? RankOfExpected, bool RecallAt1, bool RecallAt3, bool RecallAt10, double Mrr);

    public async Task EnsureTextIndexesAsync()
    {
        await Exec("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        await Exec("CREATE INDEX IF NOT EXISTS sd_trgm_body ON search_documents USING gin (body_effective gin_trgm_ops)");
        await Exec("CREATE INDEX IF NOT EXISTS sd_trgm_title ON search_documents USING gin (title_effective gin_trgm_ops)");
    }

    /// <summary>
    /// Hybrid score one query against every cluster. Text side from real SQL
    /// (ts_rank_cd + pg_trgm), vector side from real pgvector ANN over a
    /// pre-pagination candidate pool, then DATA_MODEL §6 aggregation.
    /// </summary>
    public async Task<QueryResult> RunAsync(SyntheticQuery q, List<VideoTruth> truth,
        Dictionary<Guid, float[]> centroids, Dictionary<Guid, List<DocTruth>> docsByVideo, Config cfg)
    {
        var sw = Stopwatch.StartNew();
        var qVec = _embedder.Embed(q.ExpectedTopic, q.Text, "");   // text-jitter limitation: topic signal only

        // ---- TEXT side (real SQL): per-video best ts_rank_cd + trgm over its docs ----
        var textByVideo = new Dictionary<Guid, double>();
        await using (var cmd = new NpgsqlCommand("""
            WITH scored AS (
                SELECT d.parent_video_id,
                       MAX(
                         ts_rank_cd(
                           setweight(to_tsvector('english', coalesce(d.title_effective,'')),'A') ||
                           setweight(to_tsvector('english', coalesce(d.body_effective,'')),'B'),
                           plainto_tsquery('english', $1)
                         ) * 4.0
                         + GREATEST(
                             similarity(d.title_effective, $1),
                             similarity(d.body_effective, $1)
                           ) * 0.5
                       ) AS s
                FROM search_documents d
                WHERE d.parent_video_id IS NOT NULL
                GROUP BY d.parent_video_id
            )
            SELECT parent_video_id, s FROM scored WHERE s > 0
            """, _conn))
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = q.Text });
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) textByVideo[r.GetGuid(0)] = r.GetDouble(1);
        }

        // ---- VECTOR side (real pgvector ANN): pre-pagination candidate pool, doc-level ----
        var vecByVideo = new Dictionary<Guid, double>();
        var docSimsByVideo = new Dictionary<Guid, List<double>>();   // per-doc vector sims from the real pool
        var absSims = new List<(Guid VideoId, double Sim)>();
        await using (var cmd2 = new NpgsqlCommand("""
            SELECT d.parent_video_id, 1 - (e.embedding <=> $1) AS sim
            FROM embeddings e JOIN search_documents d ON d.id = e.search_document_id
            WHERE d.parent_video_id IS NOT NULL
            ORDER BY e.embedding <=> $1
            LIMIT $2
            """, _conn))
        {
            cmd2.Parameters.Add(new NpgsqlParameter { Value = new Vector(qVec) });
            cmd2.Parameters.Add(new NpgsqlParameter { Value = cfg.CandidatePool });
            await using var r = await cmd2.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var vid = r.GetGuid(0); double sim = r.GetDouble(1);
                absSims.Add((vid, sim));
                if (!docSimsByVideo.TryGetValue(vid, out var list)) docSimsByVideo[vid] = list = new List<double>();
                list.Add(sim);
                if (!vecByVideo.TryGetValue(vid, out var cur) || sim > cur) vecByVideo[vid] = sim;
            }
        }

        // ---- relSimPct: min-max over the PRE-PAGINATION candidate pool (DATA_MODEL §6) ----
        double relMin = absSims.Count > 0 ? absSims.Min(x => x.Sim) : 0;
        double relMax = absSims.Count > 0 ? absSims.Max(x => x.Sim) : 0;
        double RelPct(double sim) => relMax == relMin ? 100.0 : 100.0 * (sim - relMin) / (relMax - relMin);

        // ---- normalize each side to [0,1] over videos (min-max) ----
        (Dictionary<Guid, double> textN, Dictionary<Guid, double> vecN) = Normalize(textByVideo, vecByVideo);

        // ---- aggregate per cluster ----
        var ranked = new List<ScoredCluster>();
        foreach (var v in truth)
        {
            var docs = docsByVideo.TryGetValue(v.VideoId, out var dl) ? dl : new List<DocTruth>();
            double docScore = cfg.TextWeight * textN.GetValueOrDefault(v.VideoId)
                            + cfg.VectorWeight * vecN.GetValueOrDefault(v.VideoId);

            // cluster base = 0.65*max + 0.25*avg(top3) + 0.10*coverage, using per-doc
            // blended scores from the two REAL signals (no extra .NET embedding calls).
            var docSims = docSimsByVideo.TryGetValue(v.VideoId, out var ds) ? ds : new List<double>();
            var perDoc = docSims.Select(sim => cfg.TextWeight * textN.GetValueOrDefault(v.VideoId) + cfg.VectorWeight * sim)
                                .OrderByDescending(x => x).ToList();
            double maxDoc = perDoc.Count > 0 ? perDoc[0] : 0;
            double avgTop3 = perDoc.Count > 0 ? perDoc.Take(3).Average() : 0;
            int distinctTypes = MatchedTypes(docs, q.Text, vecByVideo.ContainsKey(v.VideoId)).Count;
            double coverage = Math.Min(distinctTypes / 4.0, 1.0);
            double clusterBase = cfg.AggMax * maxDoc + cfg.AggTop3 * avgTop3 + cfg.AggCoverage * coverage;

            double noteBoost = docs.Any(d => d.DocType == "note" && q.Text.Contains("note", StringComparison.OrdinalIgnoreCase))
                ? cfg.NoteBoost : 0;
            double interactionBoost = Math.Min(cfg.InteractionBoostCap, 0.01 * cfg.InteractionsPerVideo);

            double clusterScore = Math.Min(1.0, clusterBase + noteBoost + interactionBoost);

            ranked.Add(new ScoredCluster(
                v.VideoId, v.Topic, v.Title,
                TextScore: Math.Round(textN.GetValueOrDefault(v.VideoId), 4),
                VectorScore: Math.Round(vecN.GetValueOrDefault(v.VideoId), 4),
                DocScore: Math.Round(docScore, 4),
                ClusterBase: Math.Round(clusterBase, 4),
                CoverageScore: Math.Round(coverage, 4),
                NoteBoost: Math.Round(noteBoost, 4),
                InteractionBoost: Math.Round(interactionBoost, 4),
                ClusterScore: Math.Round(clusterScore, 4),
                RelSimPct: Math.Round(vecByVideo.TryGetValue(v.VideoId, out var s) ? RelPct(s) : 0, 2)));
        }
        ranked = ranked.OrderByDescending(r => r.ClusterScore).ThenBy(r => r.VideoId).ToList();
        sw.Stop();

        int? rankOfExpected = null;
        for (int i = 0; i < ranked.Count; i++)
            if (ranked[i].VideoId == q.ExpectedVideoId) { rankOfExpected = i + 1; break; }

        return new QueryResult(q, ranked, sw.Elapsed.TotalMilliseconds, rankOfExpected,
            RecallAt1: rankOfExpected == 1,
            RecallAt3: rankOfExpected is >= 1 and <= 3,
            RecallAt10: rankOfExpected is >= 1 and <= 10,
            Mrr: rankOfExpected is > 0 ? 1.0 / rankOfExpected.Value : 0);
    }

    private static HashSet<string> MatchedTypes(List<DocTruth> docs, string queryText, bool vectorHit)
    {
        var set = new HashSet<string>();
        var terms = queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Select(t => t.Trim(':', ',', '.')).Where(t => t.Length > 2).ToArray();
        foreach (var d in docs)
            if (terms.Any(t => (d.Title + " " + d.Body).Contains(t, StringComparison.OrdinalIgnoreCase)))
                set.Add(d.DocType);
        if (vectorHit) set.Add("transcript_chunk"); // ANN hit implies some transcript-level match
        return set;
    }

    private static (Dictionary<Guid, double>, Dictionary<Guid, double>) Normalize(
        Dictionary<Guid, double> text, Dictionary<Guid, double> vec)
    {
        static Dictionary<Guid, double> MinMax(Dictionary<Guid, double> m)
        {
            if (m.Count == 0) return m;
            double mn = m.Values.Min(), mx = m.Values.Max();
            if (mx == mn) return m.ToDictionary(kv => kv.Key, _ => 1.0);
            return m.ToDictionary(kv => kv.Key, kv => (kv.Value - mn) / (mx - mn));
        }
        return (MinMax(text), MinMax(vec));
    }

    // ---- ADR-0012: store a query embedding, match cluster fingerprints by ABSOLUTE cosine ----
    public async Task<List<(int Topic, double AbsCosine, bool HighSignal)>> HighSignalMatchesAsync(
        SyntheticQuery q, Dictionary<Guid, float[]> centroids, List<VideoTruth> truth, double threshold)
    {
        var qVec = _embedder.Embed(q.ExpectedTopic, q.Text, "");
        var matches = new List<(int, double, bool)>();
        foreach (var v in truth)
        {
            double cos = SyntheticEmbedder.CosineSimilarity(qVec, centroids[v.VideoId]);
            matches.Add((v.Topic, Math.Round(cos, 4), cos >= threshold));
        }
        return await Task.FromResult(matches.OrderByDescending(m => m.Item2).ToList());
    }

    // ---- Related items: nearest cluster centroids excluding self ----
    public List<(Guid VideoId, int Topic, double AbsCosine, double RelPct)> RelatedAsync(
        Guid selfVideoId, Dictionary<Guid, float[]> centroids, List<VideoTruth> truth, int topN)
    {
        var self = centroids[selfVideoId];
        var sims = truth.Where(v => v.VideoId != selfVideoId)
            .Select(v => (v.VideoId, v.Topic, Cos: SyntheticEmbedder.CosineSimilarity(self, centroids[v.VideoId])))
            .OrderByDescending(x => x.Cos).Take(topN).ToList();
        double mn = sims.Min(x => x.Cos), mx = sims.Max(x => x.Cos);
        return sims.Select(x => (x.VideoId, x.Topic, Math.Round(x.Cos, 4),
            Math.Round(mx == mn ? 100.0 : 100.0 * (x.Cos - mn) / (mx - mn), 2))).ToList();
    }

    public async Task<Dictionary<Guid, float[]>> LoadCentroidsAsync()
    {
        var map = new Dictionary<Guid, float[]>();
        await using var cmd = new NpgsqlCommand(
            "SELECT video_id, embedding FROM video_cluster_embeddings WHERE model=$1", _conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = CorpusLoader.ModelId });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            map[r.GetGuid(0)] = r.GetFieldValue<Vector>(1).ToArray();
        return map;
    }

    private async Task<int> Exec(string sql, params NpgsqlParameter[] ps)
    { await using var c = new NpgsqlCommand(sql, _conn); c.Parameters.AddRange(ps); return await c.ExecuteNonQueryAsync(); }
}
