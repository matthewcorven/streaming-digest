// ============================================================
// THROWAWAY PROTOTYPE — Task 11.3a. Synthetic corpus generator.
// Seeded, template/topic/vocabulary driven, ZERO AI. Deterministic.
// ============================================================

namespace VectorPrototype;

/// <summary>One synthetic search document, pre-construction per DATA_MODEL §5.</summary>
public sealed record CorpusDocument(
    string DocumentType,          // video_metadata, segment_title, transcript_chunk, external_resource_metadata, scraped_page_text, repository_readme_chunk, note
    string SourceEntityType,      // videos, segments, transcript_cues, external_resources, scraped_pages, repository_documents, notes
    Guid SourceEntityId,
    Guid? ParentVideoId,
    string TitleEffective,
    string BodyEffective);

/// <summary>A generated video with its child documents.</summary>
public sealed record GeneratedVideo(
    Guid VideoId,
    int TopicIndex,
    List<CorpusDocument> Documents);

/// <summary>
/// Deterministic, seedable corpus generator. Controlled topic distribution so
/// vector results are attributable to the construction approach, not content noise.
/// </summary>
public sealed class CorpusGenerator
{
    private readonly Random _rng;

    // Topic vocabulary pools — each topic is a tight cluster of related terms so
    // synthetic embeddings can carry a real (if crude) semantic signal.
    public static readonly string[] TopicNames =
    [
        "dotnet-performance", "blazor-ui", "postgres-internals", "vector-search",
        "kubernetes-ops", "rust-systems", "machine-learning", "web-security",
        "distributed-systems", "devops-ci"
    ];

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

    // Controlled document-type proportions (must sum to 1.0). Mirrors DATA_MODEL §5's
    // seven document kinds. Transcript chunks dominate, as in a real video corpus.
    public static readonly (string Type, double Weight)[] DocumentMix =
    [
        ("transcript_chunk",            0.45),
        ("segment_title",               0.18),
        ("video_metadata",              0.06),
        ("external_resource_metadata",  0.08),
        ("scraped_page_text",           0.09),
        ("repository_readme_chunk",     0.08),
        ("note",                        0.06),
    ];

    public CorpusGenerator(int seed) => _rng = new Random(seed);

    /// <summary>Topic index for a video, drawn from a Zipf-ish skew so some topics dominate.</summary>
    private int PickTopic()
    {
        // Skewed distribution: earlier topics more likely. Deterministic under seed.
        double u = _rng.NextDouble();
        double skewed = Math.Pow(u, 1.6);
        return Math.Min((int)(skewed * TopicNames.Length), TopicNames.Length - 1);
    }

    private string Word(int topic) => TopicVocab[topic][_rng.Next(TopicVocab[topic].Length)];

    /// <summary>A template-driven sentence mixing topic vocab with filler.</summary>
    private string Sentence(int topic, int wordCount)
    {
        var words = new List<string>();
        for (int i = 0; i < wordCount; i++)
        {
            // ~70% topic vocab, 30% cross-topic filler to keep it realistic but topical.
            int t = _rng.NextDouble() < 0.70 ? topic : _rng.Next(TopicNames.Length);
            words.Add(Word(t));
        }
        return string.Join(' ', words);
    }

    private string Paragraph(int topic, int sentences, int wordsPerSentence) =>
        string.Join(". ", Enumerable.Range(0, sentences).Select(_ => Sentence(topic, wordsPerSentence)));

    private string PickDocumentType()
    {
        double u = _rng.NextDouble();
        double cumulative = 0;
        foreach (var (type, weight) in DocumentMix)
        {
            cumulative += weight;
            if (u <= cumulative) return type;
        }
        return DocumentMix[^1].Type;
    }

    private CorpusDocument BuildDocument(string docType, Guid videoId, int topic)
    {
        var (srcType, title, body) = docType switch
        {
            "video_metadata" => ("videos",
                $"Video about {Word(topic)} and {Word(topic)}",
                Paragraph(topic, 3, 8)),
            "segment_title" => ("segments",
                $"Segment: {Word(topic)} deep dive",
                $"Summary covering {Word(topic)}, {Word(topic)}, and {Word(topic)}."),
            "transcript_chunk" => ("transcript_cues",
                "",
                Paragraph(topic, 5, 9)),
            "external_resource_metadata" => ("external_resources",
                $"{Word(topic)} tool",
                $"A resource about {Word(topic)} in domain {Word(topic)}."),
            "scraped_page_text" => ("scraped_pages",
                $"{Word(topic)} documentation",
                Paragraph(topic, 6, 8)),
            "repository_readme_chunk" => ("repository_documents",
                $"{Word(topic)}-lib README",
                Paragraph(topic, 4, 8)),
            "note" => ("notes",
                "",
                $"Note to self: revisit {Word(topic)} and compare {Word(topic)} with {Word(topic)}."),
            _ => throw new InvalidOperationException(docType)
        };

        return new CorpusDocument(
            DocumentType: docType,
            SourceEntityType: srcType,
            SourceEntityId: Guid.NewGuid(),
            ParentVideoId: videoId,
            TitleEffective: title,
            BodyEffective: body);
    }

    /// <summary>
    /// Generate <paramref name="videoCount"/> videos with controlled-proportion documents.
    /// Returns documents grouped by parent video for cluster construction.
    /// </summary>
    public List<GeneratedVideo> Generate(int videoCount, int docsPerVideo)
    {
        var videos = new List<GeneratedVideo>(videoCount);
        for (int v = 0; v < videoCount; v++)
        {
            int topic = PickTopic();
            var videoId = Guid.NewGuid();
            var docs = new List<CorpusDocument>(docsPerVideo);

            // Every video gets exactly one video_metadata doc; the rest follow the mix.
            docs.Add(BuildDocument("video_metadata", videoId, topic));
            for (int d = 1; d < docsPerVideo; d++)
                docs.Add(BuildDocument(PickDocumentType(), videoId, topic));

            videos.Add(new GeneratedVideo(videoId, topic, docs));
        }
        return videos;
    }
}
