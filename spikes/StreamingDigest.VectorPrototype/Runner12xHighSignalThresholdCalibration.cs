using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StreamingDigest.Application;

namespace VectorPrototype;

public static class Runner12xHighSignalThresholdCalibration
{
    private const string DefaultModel = "bge-m3";
    private const string DefaultEndpoint = "http://localhost:11434/api/embed";
    private const string EvidenceSlug = "12.x-high-signal-threshold-calibration";

    private static readonly IReadOnlyList<CalibrationCandidate> Candidates =
    [
        new(
            "blazor-js-interop",
            "Blazor JS interop for browser APIs",
            """
            Title: Blazor JS interop for browser APIs
            Fingerprint: blazor webassembly javascript interop browser apis clipboard local storage resize observer component lifecycle
            """),
        new(
            "blazor-state",
            "Blazor component state and form flows",
            """
            Title: Blazor component state and form flows
            Fingerprint: blazor component state forms validation editform cascading values rerender behavior render lifecycle
            """),
        new(
            "aspnet-cookie-auth",
            "ASP.NET Core cookie authentication",
            """
            Title: ASP.NET Core cookie authentication
            Fingerprint: aspnet core cookie authentication sign in auth cookie sliding expiration secure cookie settings login flow
            """),
        new(
            "aspnet-jwt-auth",
            "ASP.NET Core JWT bearer authentication",
            """
            Title: ASP.NET Core JWT bearer authentication
            Fingerprint: aspnet core jwt bearer token api authentication authorization policy claims protected endpoints
            """),
        new(
            "pgvector-hnsw",
            "pgvector HNSW versus IVFFlat",
            """
            Title: pgvector HNSW versus IVFFlat
            Fingerprint: pgvector hnsw ivfflat vector index similarity search nearest neighbor recall probes lists build time
            """),
        new(
            "hybrid-ranking",
            "Hybrid search ranking with vectors and keywords",
            """
            Title: Hybrid search ranking with vectors and keywords
            Fingerprint: hybrid search ranking keyword relevance trigram similarity vector cosine result blending search formula
            """),
        new(
            "azure-monitor-kql",
            "Azure Monitor KQL and alerts",
            """
            Title: Azure Monitor KQL and alerts
            Fingerprint: azure monitor application insights kql alerts failures traces log analytics dashboard queries
            """),
        new(
            "opentelemetry-tracing",
            "OpenTelemetry tracing and logs",
            """
            Title: OpenTelemetry tracing and logs
            Fingerprint: opentelemetry tracing spans structured logs exporters distributed tracing correlation telemetry instrumentation
            """),
        new(
            "efcore-query-tuning",
            "EF Core query tuning",
            """
            Title: EF Core query tuning
            Fingerprint: ef core query tuning projections asnotracking filtered include split query read performance optimization
            """),
        new(
            "postgres-index-tuning",
            "PostgreSQL indexing for search workloads",
            """
            Title: PostgreSQL indexing for search workloads
            Fingerprint: postgresql gin index trigram index query planner search workloads performance tuning indexing strategies
            """),
        new(
            "k8s-autoscaling",
            "Kubernetes autoscaling and rollouts",
            """
            Title: Kubernetes autoscaling and rollouts
            Fingerprint: kubernetes horizontal pod autoscaling hpa deployment rollout replicas cluster capacity scaling strategy
            """),
        new(
            "k8s-observability",
            "Kubernetes observability with Prometheus and Grafana",
            """
            Title: Kubernetes observability with Prometheus and Grafana
            Fingerprint: kubernetes observability prometheus grafana metrics dashboards pod health alerts monitoring
            """)
    ];

    private static readonly IReadOnlyList<CalibrationSearch> Searches =
    [
        new("search-01", "blazor wasm javascript interop for clipboard and local storage", "blazor-js-interop", "Targeted browser API search."),
        new("search-02", "blazor js interop wrappers for browser apis clipboard and local storage", "blazor-js-interop", "Close paraphrase of the intended interop topic."),
        new("search-03", "blazor editform validation and component state management", "blazor-state", "Blazor near-miss pair with forms/state focus."),
        new("search-04", "blazor component state rerenders and editform validation", "blazor-state", "State-management phrasing that stays within the high-signal bar."),
        new("search-05", "aspnet core cookie sign in flow and auth cookie expiration", "aspnet-cookie-auth", "Cookie-auth query paired against JWT near miss."),
        new("search-06", "jwt bearer token authentication for an aspnet core api", "aspnet-jwt-auth", "Bearer-token API query paired against cookie-auth near miss."),
        new("search-07", "pgvector hnsw versus ivfflat for nearest neighbor recall", "pgvector-hnsw", "Index-comparison search paired against hybrid-search near miss."),
        new("search-08", "hybrid search ranking with vector similarity and keyword matching", "hybrid-ranking", "Hybrid-ranking search paired against vector-index near miss."),
        new("search-09", "kql query for failures traces and alerts in azure monitor", "azure-monitor-kql", "Azure Monitor query paired against OpenTelemetry observability near miss."),
        new("search-10", "distributed tracing spans and structured logs with opentelemetry", "opentelemetry-tracing", "Tracing query paired against Azure Monitor observability near miss."),
        new("search-11", "ef core projections asnotracking filtered include and split query performance", "efcore-query-tuning", "EF Core query-performance search paired against PostgreSQL indexing near miss."),
        new("search-12", "postgresql gin trigram index and query planner tuning for search workloads", "postgres-index-tuning", "PostgreSQL indexing search paired against EF Core query-tuning near miss."),
        new("search-13", "kubernetes horizontal pod autoscaling and deployment rollout strategy", "k8s-autoscaling", "Autoscaling query paired against Kubernetes observability near miss."),
        new("search-14", "prometheus grafana dashboards and alerts for kubernetes pod health", "k8s-observability", "Kubernetes observability query paired against autoscaling near miss.")
    ];

    public static async Task RunAsync()
    {
        Console.WriteLine("\n=== Issue #100 high-signal threshold calibration ===");
        Console.WriteLine("Using the production OllamaEmbeddingService against curated search/fingerprint pairs.");

        EnsureProcessDefaults();

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        var configuration = new ConfigurationBuilder().Build();
        var embeddingService = new OllamaEmbeddingService(httpClient, configuration);

        var candidateEmbeddings = new Dictionary<string, EmbeddingGenerationResult>(StringComparer.Ordinal);
        foreach (var candidate in Candidates)
        {
            candidateEmbeddings[candidate.Id] = await embeddingService.GenerateEmbeddingAsync(candidate.Fingerprint);
        }

        var queryEmbeddings = new Dictionary<string, EmbeddingGenerationResult>(StringComparer.Ordinal);
        foreach (var search in Searches)
        {
            queryEmbeddings[search.Id] = await embeddingService.GenerateEmbeddingAsync(search.Query);
        }

        var dimensions = candidateEmbeddings.Values.Select(value => value.Dimensions)
            .Concat(queryEmbeddings.Values.Select(value => value.Dimensions))
            .Distinct()
            .Single();
        var model = candidateEmbeddings.Values.Select(value => value.Model)
            .Concat(queryEmbeddings.Values.Select(value => value.Model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Single();

        var caseResults = new List<CalibrationCaseResult>(Searches.Count);
        foreach (var search in Searches)
        {
            var queryVector = queryEmbeddings[search.Id].Values;
            var intendedCandidate = Candidates.Single(candidate => candidate.Id == search.IntendedCandidateId);
            var intendedSimilarity = CosineSimilarity(queryVector, candidateEmbeddings[intendedCandidate.Id].Values);

            var bestOther = Candidates
                .Where(candidate => candidate.Id != intendedCandidate.Id)
                .Select(candidate => new CandidateScore(
                    candidate.Id,
                    candidate.Title,
                    CosineSimilarity(queryVector, candidateEmbeddings[candidate.Id].Values)))
                .OrderByDescending(score => score.Similarity)
                .First();

            caseResults.Add(new CalibrationCaseResult(
                search.Id,
                search.Query,
                search.Rationale,
                intendedCandidate.Id,
                intendedCandidate.Title,
                intendedSimilarity,
                bestOther));
        }

        var thresholdRows = Enumerable.Range(70, 29)
            .Select(percent =>
            {
                var threshold = percent / 100d;
                var ownPassCount = caseResults.Count(result => result.IntendedSimilarity >= threshold);
                var otherPassCount = caseResults.Count(result => result.BestOther.Similarity >= threshold);
                return new ThresholdSweepRow(
                    percent,
                    threshold,
                    ownPassCount,
                    Math.Round((double)ownPassCount / caseResults.Count, 4),
                    otherPassCount,
                    Math.Round((double)otherPassCount / caseResults.Count, 4));
            })
            .ToList();

        var ownScores = caseResults.Select(result => result.IntendedSimilarity).OrderBy(value => value).ToArray();
        var otherScores = caseResults.Select(result => result.BestOther.Similarity).OrderBy(value => value).ToArray();
        var minOwn = ownScores[0];
        var maxBestOther = otherScores[^1];
        var gapWidth = minOwn - maxBestOther;

        var recommendedRow = thresholdRows
            .Where(row => row.BestOtherPassCount == 0)
            .OrderByDescending(row => row.OwnPassCount)
            .ThenBy(row => row.ThresholdPercent)
            .First();

        var recommendedThreshold = new RecommendedThreshold(
            recommendedRow.ThresholdPercent,
            recommendedRow.Threshold,
            gapWidth,
            minOwn,
            maxBestOther,
            recommendedRow.OwnPassCount,
            recommendedRow.OwnPassRate,
            recommendedRow.BestOtherPassCount,
            recommendedRow.BestOtherPassRate,
            $"Selected the lowest whole-percent threshold that kept best-other pass at 0/{caseResults.Count} while preserving the highest own-pass count ({recommendedRow.OwnPassCount}/{caseResults.Count}).");

        Console.WriteLine($"model={model} dimensions={dimensions} endpoint={ResolveEndpoint()}");
        Console.WriteLine($"own similarity range: {ownScores[0]:F4} .. {ownScores[^1]:F4}");
        Console.WriteLine($"best-other range:    {otherScores[0]:F4} .. {otherScores[^1]:F4}");
        Console.WriteLine($"observed gap:        {gapWidth:F4} (own min {minOwn:F4} - best-other max {maxBestOther:F4})");
        Console.WriteLine($"recommended:         {recommendedRow.Threshold:F2} ({recommendedRow.ThresholdPercent}%) -> own {recommendedRow.OwnPassCount}/{caseResults.Count}, best-other {recommendedRow.BestOtherPassCount}/{caseResults.Count}");
        Console.WriteLine("\nThreshold sweep:");
        foreach (var row in thresholdRows)
        {
            Console.WriteLine($"  {row.Threshold:F2}  own={row.OwnPassCount}/{caseResults.Count} ({row.OwnPassRate:F4})  best-other={row.BestOtherPassCount}/{caseResults.Count} ({row.BestOtherPassRate:F4})");
        }

        var evidence = new CalibrationEvidence(
            Task: EvidenceSlug,
            GeneratedAtUtc: DateTime.UtcNow,
            Provider: "ollama",
            Endpoint: ResolveEndpoint(),
            Model: model,
            Dimensions: dimensions,
            CandidateCount: Candidates.Count,
            SearchCount: Searches.Count,
            CandidateTitles: Candidates.Select(candidate => new CandidateCatalogEntry(candidate.Id, candidate.Title)).ToArray(),
            Cases: caseResults.ToArray(),
            OwnDistribution: BuildDistribution(ownScores),
            BestOtherDistribution: BuildDistribution(otherScores),
            RecommendedThreshold: recommendedThreshold,
            ThresholdSweep: thresholdRows.ToArray());

        var outDir = ResolveEvidenceDirectory();
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, $"{EvidenceSlug}.json");
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outPath, json);
        Console.WriteLine($"\nevidence json -> {outPath}");
    }

    private static void EnsureProcessDefaults()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_MODEL")))
        {
            Environment.SetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_MODEL", DefaultModel);
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OLLAMA_BASE_URL"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OLLAMA_HOST")))
        {
            Environment.SetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT", DefaultEndpoint);
        }
    }

    private static string ResolveEndpoint()
    {
        return Environment.GetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_HOST")
            ?? DefaultEndpoint;
    }

    private static string ResolveEvidenceDirectory()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var current = dir; current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "docs", "verification");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "docs", "verification");
    }

    private static DistributionSummary BuildDistribution(double[] orderedValues)
    {
        return new DistributionSummary(
            Min: orderedValues[0],
            P25: Percentile(orderedValues, 0.25),
            Median: Percentile(orderedValues, 0.50),
            P75: Percentile(orderedValues, 0.75),
            Max: orderedValues[^1],
            Mean: orderedValues.Average());
    }

    private static double Percentile(double[] orderedValues, double percentile)
    {
        if (orderedValues.Length == 1)
        {
            return orderedValues[0];
        }

        var position = (orderedValues.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return orderedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return orderedValues[lowerIndex] + ((orderedValues[upperIndex] - orderedValues[lowerIndex]) * fraction);
    }

    private static double CosineSimilarity(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count)
        {
            throw new InvalidOperationException($"Embedding dimension mismatch: left={left.Count}, right={right.Count}.");
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            throw new InvalidOperationException("Cannot compute cosine similarity for a zero-length embedding.");
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    private sealed record CalibrationCandidate(string Id, string Title, string Fingerprint);

    private sealed record CalibrationSearch(string Id, string Query, string IntendedCandidateId, string Rationale);

    public sealed record CandidateCatalogEntry(string Id, string Title);

    public sealed record CandidateScore(string Id, string Title, double Similarity);

    public sealed record CalibrationCaseResult(
        string Id,
        string Query,
        string Rationale,
        string IntendedCandidateId,
        string IntendedCandidateTitle,
        double IntendedSimilarity,
        CandidateScore BestOther);

    public sealed record ThresholdSweepRow(
        int ThresholdPercent,
        double Threshold,
        int OwnPassCount,
        double OwnPassRate,
        int BestOtherPassCount,
        double BestOtherPassRate);

    public sealed record DistributionSummary(
        double Min,
        double P25,
        double Median,
        double P75,
        double Max,
        double Mean);

    public sealed record RecommendedThreshold(
        int ThresholdPercent,
        double Threshold,
        double ObservedGapWidth,
        double MinOwnSimilarity,
        double MaxBestOtherSimilarity,
        int OwnPassCount,
        double OwnPassRate,
        int BestOtherPassCount,
        double BestOtherPassRate,
        string Rationale);

    public sealed record CalibrationEvidence(
        string Task,
        DateTime GeneratedAtUtc,
        string Provider,
        string Endpoint,
        string Model,
        int Dimensions,
        int CandidateCount,
        int SearchCount,
        IReadOnlyList<CandidateCatalogEntry> CandidateTitles,
        IReadOnlyList<CalibrationCaseResult> Cases,
        DistributionSummary OwnDistribution,
        DistributionSummary BestOtherDistribution,
        RecommendedThreshold RecommendedThreshold,
        IReadOnlyList<ThresholdSweepRow> ThresholdSweep);
}
