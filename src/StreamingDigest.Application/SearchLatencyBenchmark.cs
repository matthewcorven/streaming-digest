using System.Diagnostics;

namespace StreamingDigest.Application;

public static class SearchLatencyBenchmark
{
    private static readonly string[] QueryTemplates =
    [
        "half remembered walkthrough about {0} {1} {2}",
        "searching for the clip that covered {0}, {1}, and {2}",
        "project idea query around {0} and {1} with {2}",
        "which video explained {0} plus {1} near {2}",
        "find the segment about {0} {1} {2}"
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "your", "about", "video", "videos",
        "clip", "search", "result", "results", "cluster", "clusters", "segment", "segments", "notes",
        "note", "website", "websites", "repository", "repositories", "related", "items", "using", "used",
        "around", "through", "them", "they", "their", "same", "when", "while", "still"
    };

    public static SearchLatencyBenchmarkSuite RunRepresentativeBenchmark(SearchLatencyBenchmarkOptions? options = null)
    {
        var effectiveOptions = options ?? new SearchLatencyBenchmarkOptions();
        var reports = effectiveOptions.CorpusVideoCounts
            .Select(videoCount => BenchmarkCorpus(videoCount, effectiveOptions))
            .ToList();

        return new SearchLatencyBenchmarkSuite
        {
            Task = "12.8-search-latency",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            RankingFormulaVersion = HybridRankingService.FormulaVersion,
            CandidateScoringVersion = SearchUiService.CandidateScoringVersion,
            DocumentConstructionVersion = SearchDocumentGenerator.DocumentConstructionVersion,
            ProgressIndicatorDelayMs = (int)effectiveOptions.ProgressIndicatorDelay.TotalMilliseconds,
            Corpora = reports
        };
    }

    private static SearchLatencyCorpusReport BenchmarkCorpus(int videoCount, SearchLatencyBenchmarkOptions options)
    {
        var corpus = SearchRecallRepresentativeCorpusFactory.CreateRepresentativeCorpus(videoCount, options.CorpusSeed).ToArray();
        var service = new SearchUiService(corpus);
        var queries = CreateRepresentativeQueries(corpus, options.QueryCount, options.QuerySeed);

        foreach (var warmupQuery in queries.Take(options.WarmupQueryCount))
        {
            _ = service.Search(new SearchRequest
            {
                Query = warmupQuery,
                Filters = new SearchFilters
                {
                    ResultType = "video"
                }
            });
        }

        var measurementsMs = new List<double>(queries.Count * options.MeasurementPasses);
        for (var pass = 0; pass < options.MeasurementPasses; pass++)
        {
            foreach (var query in queries)
            {
                var stopwatch = Stopwatch.StartNew();
                _ = service.Search(new SearchRequest
                {
                    Query = query,
                    Filters = new SearchFilters
                    {
                        ResultType = "video"
                    }
                });
                stopwatch.Stop();
                measurementsMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        var target = GetLatencyTarget(videoCount);
        return new SearchLatencyCorpusReport
        {
            VideoCount = videoCount,
            QueryCount = queries.Count,
            MeasurementCount = measurementsMs.Count,
            TargetP50Ms = target.P50Ms,
            TargetP95Ms = target.P95Ms,
            P50Ms = Percentile(measurementsMs, 0.50),
            P95Ms = Percentile(measurementsMs, 0.95),
            AverageMs = measurementsMs.Average(),
            MaxMs = measurementsMs.Max(),
            EntityCounts = BuildEntityCounts(corpus),
            SampleQueries = queries.Take(5).ToArray()
        };
    }

    private static SearchLatencyTarget GetLatencyTarget(int videoCount)
    {
        return videoCount < 500
            ? new SearchLatencyTarget(P50Ms: 2000, P95Ms: 5000)
            : videoCount < 1000
                ? new SearchLatencyTarget(P50Ms: 2000, P95Ms: 5000)
                : new SearchLatencyTarget(P50Ms: 3000, P95Ms: 10000);
    }

    private static SearchLatencyEntityCounts BuildEntityCounts(IReadOnlyList<SearchCorpusClusterSeed> corpus)
    {
        var searchDocuments = corpus.Sum(cluster => cluster.Documents.Count);
        var repositoryLinks = corpus.Sum(cluster => cluster.RepositoryLinks.Count);
        var websiteLinks = corpus.Sum(cluster => cluster.WebsiteLinks.Count);

        return new SearchLatencyEntityCounts
        {
            Segments = corpus.Sum(cluster => cluster.Submatches.Count(match => string.Equals(match.Type, "segment", StringComparison.OrdinalIgnoreCase))),
            TranscriptClusters = corpus.Count(cluster => cluster.HasTranscript),
            Links = repositoryLinks + websiteLinks,
            RepositoryLinks = repositoryLinks,
            NoteClusters = corpus.Count(cluster => cluster.HasNotes),
            SearchDocuments = searchDocuments,
            Embeddings = searchDocuments
        };
    }

    private static IReadOnlyList<string> CreateRepresentativeQueries(
        IReadOnlyList<SearchCorpusClusterSeed> corpus,
        int queryCount,
        int seed)
    {
        var random = new Random(seed);
        var queries = new List<string>(queryCount);
        var orderedCorpus = corpus
            .OrderBy(cluster => cluster.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < queryCount; index++)
        {
            var cluster = orderedCorpus[index % orderedCorpus.Length];
            var keywords = ExtractKeywords(cluster);
            var template = QueryTemplates[index % QueryTemplates.Length];
            queries.Add(string.Format(template, keywords[0], keywords[1], keywords[2]));

            if (index % orderedCorpus.Length == orderedCorpus.Length - 1)
            {
                orderedCorpus = orderedCorpus.OrderBy(_ => random.Next()).ToArray();
            }
        }

        return queries;
    }

    private static string[] ExtractKeywords(SearchCorpusClusterSeed cluster)
    {
        var keywordPool = string.Join(
                " ",
                cluster.Title,
                cluster.PrimaryMatch,
                cluster.PrimaryMatchTimestamp,
                cluster.Submatches.FirstOrDefault()?.Detail,
                cluster.Documents.FirstOrDefault()?.Text,
                cluster.Documents.FirstOrDefault()?.Snippet)
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '-', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= 4 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        if (keywordPool.Length >= 3)
        {
            return [keywordPool[0], keywordPool[1], keywordPool[2]];
        }

        return ["ranking", "search", "recall"];
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var index = (sorted.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(index);
        var upperIndex = (int)Math.Ceiling(index);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        var fraction = index - lowerIndex;
        return sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * fraction);
    }

    private sealed record SearchLatencyTarget(double P50Ms, double P95Ms);
}

public sealed class SearchLatencyBenchmarkOptions
{
    public IReadOnlyList<int> CorpusVideoCounts { get; set; } = [500, 2000];
    public int QueryCount { get; set; } = 240;
    public int WarmupQueryCount { get; set; } = 12;
    public int MeasurementPasses { get; set; } = 3;
    public int CorpusSeed { get; set; } = 31;
    public int QuerySeed { get; set; } = 128;
    public TimeSpan ProgressIndicatorDelay { get; set; } = TimeSpan.FromSeconds(1);
}

public sealed class SearchLatencyBenchmarkSuite
{
    public string Task { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string RankingFormulaVersion { get; set; } = string.Empty;
    public string CandidateScoringVersion { get; set; } = string.Empty;
    public string DocumentConstructionVersion { get; set; } = string.Empty;
    public int ProgressIndicatorDelayMs { get; set; }
    public IReadOnlyList<SearchLatencyCorpusReport> Corpora { get; set; } = Array.Empty<SearchLatencyCorpusReport>();
}

public sealed class SearchLatencyCorpusReport
{
    public int VideoCount { get; set; }
    public int QueryCount { get; set; }
    public int MeasurementCount { get; set; }
    public double TargetP50Ms { get; set; }
    public double TargetP95Ms { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double AverageMs { get; set; }
    public double MaxMs { get; set; }
    public SearchLatencyEntityCounts EntityCounts { get; set; } = new();
    public IReadOnlyList<string> SampleQueries { get; set; } = Array.Empty<string>();
    public bool MeetsLatencyTarget => P50Ms <= TargetP50Ms && P95Ms <= TargetP95Ms;
}

public sealed class SearchLatencyEntityCounts
{
    public int Segments { get; set; }
    public int TranscriptClusters { get; set; }
    public int Links { get; set; }
    public int RepositoryLinks { get; set; }
    public int NoteClusters { get; set; }
    public int SearchDocuments { get; set; }
    public int Embeddings { get; set; }
}
