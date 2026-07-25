// ============================================================
// THROWAWAY PROTOTYPE — Task 11.3b (GitHub issue #19)
// Programmatic synthetic QUERY generator. ZERO AI. Seeded.
//
// Queries are generated ANSWER-SIDE (from a known target video's topic
// and vocabulary) so ground truth is real. Deliberately NOT just doc
// excerpts: the hard case per DATA_MODEL §6 / PRD §4.2 is the VAGUE,
// natural-language query whose words may barely appear in the document
// text. So VagueIntent templates use intent framing + only sparse topic
// vocabulary, and Paraphrase templates substitute synonyms/reorderings.
//
// HONESTY NOTE (synthetic boundary): the SyntheticEmbedder jitters from a
// hash of the raw query text. A paraphrase or vague query therefore gets
// jitter that is ~uncorrelated with the target doc's jitter — the synthetic
// model has no paraphrase invariance. So recall differences across query
// KINDS are NOT meaningful measures of semantic robustness. What IS
// meaningful: that the scoring formulas, normalization, aggregation, and
// threshold plumbing compute correct numbers end-to-end. Paraphrase/vague
// semantic recall must be re-measured with the real embedding model.
// ============================================================

namespace VectorPrototype;

public enum QueryKind { Paraphrase, VagueIntent, Keyword, NoteMatch, CrossTopic }

public sealed record SyntheticQuery(
    Guid Id,
    QueryKind Kind,
    string Text,
    Guid ExpectedVideoId,
    int ExpectedTopic,
    string Rationale);

public sealed class QueryGenerator
{
    private readonly Random _rng;
    public QueryGenerator(int seed) => _rng = new Random(seed);

    // Cross-topic stopwords + filler so vague queries read naturally but stay seeded.
    private static readonly string[] Fillers =
        ["how to", "what is", "explain", "best way to", "introduction to", "deep dive into",
         "understanding", "getting started with", "tips for", "walkthrough of", "overview of",
         "troubleshooting", "optimizing", "scaling", "securing", "debugging", "comparing"];

    // Topic vocab mirrors CorpusGenerator (kept in sync deliberately — same truth source).
    private static readonly string[][] TopicVocab =
    [
        ["gc","jit","span","memory","allocation","benchmark","throughput","latency","async","pooling","simd","profiler"],
        ["component","render","binding","state","wasm","fluent","template","event","parameter","lifecycle","css","interop"],
        ["index","query","planner","wal","vacuum","buffer","replication","transaction","lock","table","partition","statistics"],
        ["embedding","vector","similarity","cosine","hnsw","ivfflat","distance","recall","ann","index","dimension","retrieval"],
        ["pod","deployment","service","ingress","helm","node","namespace","replica","autoscaler","configmap","secret","cluster"],
        ["ownership","borrow","lifetime","trait","cargo","unsafe","macro","iterator","generic","enum","pattern","concurrency"],
        ["model","training","inference","tensor","gradient","dataset","epoch","transformer","attention","loss","optimizer","neural"],
        ["xss","csrf","oauth","token","encryption","vulnerability","authentication","authorization","header","injection","cors","tls"],
        ["consensus","partition","replication","leader","raft","paxos","gossip","shard","quorum","latency","fault","cap"],
        ["pipeline","build","deploy","artifact","runner","cache","stage","trigger","release","workflow","container","registry"]
    ];

    private string V(int topic) => TopicVocab[topic][_rng.Next(TopicVocab[topic].Length)];
    private string Filler() => Fillers[_rng.Next(Fillers.Length)];

    /// <summary>Pick target videos with topic coverage (round-robin across topics present in corpus).</summary>
    public List<SyntheticQuery> Generate(List<VideoTruth> videos, int perKind)
    {
        var byTopic = videos.GroupBy(v => v.Topic).OrderBy(g => g.Key).ToList();
        var queries = new List<SyntheticQuery>();
        foreach (var kind in Enum.GetValues<QueryKind>())
            for (int i = 0; i < perKind; i++)
            {
                var target = byTopic[i % byTopic.Count].ElementAt(_rng.Next(byTopic[i % byTopic.Count].Count()));
                queries.Add(Build(kind, target));
            }
        return queries;
    }

    private SyntheticQuery Build(QueryKind kind, VideoTruth t)
    {
        string text = kind switch
        {
            // Paraphrase: intent phrasing over the video's own title words, lightly reworded.
            QueryKind.Paraphrase =>
                $"{Filler()} {V(t.Topic)} and {V(t.Topic)}" + (_rng.NextDouble() < 0.5 ? $" explained simply" : $" for beginners"),

            // VagueIntent: the killer-journey case. Minimal literal overlap with doc text.
            QueryKind.VagueIntent =>
                _rng.Next(3) switch
                {
                    0 => $"that talk about making {V(t.Topic)} work better",
                    1 => $"something about {V(t.Topic)} problems",
                    _ => $"how people handle {V(t.Topic)} in practice"
                },

            // Keyword: bare terms, heavy topic signal (easy case — sanity floor).
            QueryKind.Keyword => $"{V(t.Topic)} {V(t.Topic)} {V(t.Topic)}",

            // NoteMatch: phrased like a user's note-to-self (note_boost target).
            QueryKind.NoteMatch => $"note: revisit {V(t.Topic)} and compare {V(t.Topic)}",

            // CrossTopic: target topic mixed with a DIFFERENT topic's vocab (confusion probe).
            QueryKind.CrossTopic =>
                $"{V(t.Topic)} versus {V((t.Topic + 1 + _rng.Next(TopicVocab.Length - 1)) % TopicVocab.Length)}",
            _ => throw new InvalidOperationException()
        };
        return new SyntheticQuery(Guid.NewGuid(), kind, text, t.VideoId, t.Topic,
            Rationale: $"{kind} targeting topic {CorpusGenerator.TopicNames[t.Topic]}");
    }
}
