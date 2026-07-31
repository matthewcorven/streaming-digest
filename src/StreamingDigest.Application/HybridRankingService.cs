namespace StreamingDigest.Application;

public sealed class HybridRankingService
{
    public const string FormulaVersion = "hybrid-max-top3-coverage-v1";
    private readonly HybridRankingOptions _options;

    public HybridRankingService(HybridRankingOptions? options = null)
    {
        _options = options ?? HybridRankingOptions.Default;
        ValidateOptions(_options);
    }

    public IReadOnlyList<HybridClusterRankingResult> Rank(IReadOnlyList<HybridClusterCandidate> clusters)
        => Rank(clusters, relativeSimilarityPoolScores: null);

    public IReadOnlyList<HybridClusterRankingResult> Rank(IReadOnlyList<HybridClusterCandidate> clusters, IReadOnlyList<double>? relativeSimilarityPoolScores)
    {
        ArgumentNullException.ThrowIfNull(clusters);

        if (clusters.Count == 0)
        {
            return Array.Empty<HybridClusterRankingResult>();
        }

        var validatedClusters = ValidateClusters(clusters);
        if (validatedClusters.Count == 0)
        {
            return Array.Empty<HybridClusterRankingResult>();
        }

        var allDocuments = validatedClusters.SelectMany(cluster => cluster.Documents).ToList();
        if (allDocuments.Count == 0)
        {
            return Array.Empty<HybridClusterRankingResult>();
        }

        var normalizedTextScores = Normalize(allDocuments.Select(document => document.TextScore).ToList());
        var normalizedVectorScores = Normalize(allDocuments.Select(document => document.VectorScore).ToList());

        var documentScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var documentNormalizedTextScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var documentNormalizedVectorScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < allDocuments.Count; index++)
        {
            var document = allDocuments[index];
            var normalizedTextScore = normalizedTextScores[index];
            var normalizedVectorScore = normalizedVectorScores[index];
            var documentScore = (_options.TextWeight * normalizedTextScore) + (_options.VectorWeight * normalizedVectorScore);

            documentScores[document.Id] = documentScore;
            documentNormalizedTextScores[document.Id] = normalizedTextScore;
            documentNormalizedVectorScores[document.Id] = normalizedVectorScore;
        }

        var relativeSimilarityValues = relativeSimilarityPoolScores is not null
            ? relativeSimilarityPoolScores.ToList()
            : validatedClusters.Select(cluster => cluster.Documents.Max(document => document.VectorScore)).ToList();
        ValidateRelativeSimilarityScores(relativeSimilarityValues);

        var rankingResults = new List<HybridClusterRankingResult>(validatedClusters.Count);

        foreach (var cluster in validatedClusters)
        {
            var scoredDocuments = cluster.Documents
                .Select(document => new HybridScoredDocument(
                    Id: document.Id,
                    DocumentType: document.DocumentType,
                    MatchedFields: document.MatchedFields,
                    Snippet: document.Snippet,
                    TextScore: documentNormalizedTextScores[document.Id],
                    VectorScore: documentNormalizedVectorScores[document.Id],
                    Score: documentScores[document.Id]))
                .OrderByDescending(document => document.Score)
                .ToList();

            if (scoredDocuments.Count == 0)
            {
                continue;
            }

            var maxDocumentScore = scoredDocuments[0].Score;
            var averageTopThreeScore = scoredDocuments.Take(3).Average(document => document.Score);
            var distinctDocumentTypes = scoredDocuments.Select(document => document.DocumentType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var coverageScore = Math.Min(distinctDocumentTypes / 4.0, 1.0);
            var baseScore = (_options.AggregateMaxWeight * maxDocumentScore)
                + (_options.AggregateTopThreeWeight * averageTopThreeScore)
                + (_options.AggregateCoverageWeight * coverageScore);
            var noteBoost = cluster.HasMatchingNote ? _options.NoteBoost : 0.0;
            var interactionBoost = Math.Min(_options.InteractionBoostCap, 0.01 * cluster.RecentOpenCount);
            var finalScore = Math.Min(1.0, baseScore + noteBoost + interactionBoost);
            var rawClusterVectorScore = cluster.Documents.Max(document => document.VectorScore);

            var matchedFields = scoredDocuments
                .SelectMany(document => document.MatchedFields ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var snippets = scoredDocuments
                .Where(document => !string.IsNullOrWhiteSpace(document.Snippet))
                .Select(document => new HybridSnippet(document.DocumentType, document.Snippet!, document.MatchedFields ?? Array.Empty<string>()))
                .ToList();

            rankingResults.Add(new HybridClusterRankingResult(
                ClusterId: cluster.Id,
                ClusterTitle: cluster.Title,
                Score: Math.Round(finalScore, 4),
                ScoreComponents: new HybridScoreComponents(
                    MaxDocumentScore: Math.Round(maxDocumentScore, 4),
                    AverageTopThreeDocumentScore: Math.Round(averageTopThreeScore, 4),
                    CoverageScore: Math.Round(coverageScore, 4),
                    NoteBoost: Math.Round(noteBoost, 4),
                    InteractionBoost: Math.Round(interactionBoost, 4),
                    BaseScore: Math.Round(baseScore, 4)),
                MatchedFields: matchedFields,
                Explanation: BuildExplanation(baseScore, coverageScore, noteBoost, interactionBoost, finalScore),
                Snippets: snippets,
                RelativeSimilarityPercent: Math.Round(NormalizeClusterScore(rawClusterVectorScore, relativeSimilarityValues), 2),
                RelativeSimilarityLabel: "Relative similarity",
                RelativeSimilarityTooltip: "Relative similarity is a normalized vector rank score within the current result set and is relative to the query, model, and result set rather than a confidence score."));
        }

        return rankingResults
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.ClusterId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateOptions(HybridRankingOptions options)
    {
        if (!double.IsFinite(options.TextWeight) || !double.IsFinite(options.VectorWeight)
            || !double.IsFinite(options.AggregateMaxWeight) || !double.IsFinite(options.AggregateTopThreeWeight)
            || !double.IsFinite(options.AggregateCoverageWeight) || !double.IsFinite(options.NoteBoost)
            || !double.IsFinite(options.InteractionBoostCap))
        {
            throw new ArgumentException("Ranking weights must be finite values.", nameof(options));
        }

        if (options.TextWeight < 0 || options.VectorWeight < 0 || options.AggregateMaxWeight < 0
            || options.AggregateTopThreeWeight < 0 || options.AggregateCoverageWeight < 0 || options.NoteBoost < 0
            || options.InteractionBoostCap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Ranking weights must be non-negative.");
        }

        if (options.TextWeight + options.VectorWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Text and vector weights must sum to a positive value.");
        }
    }

    private static List<HybridClusterCandidate> ValidateClusters(IReadOnlyList<HybridClusterCandidate> clusters)
    {
        var validatedClusters = new List<HybridClusterCandidate>(clusters.Count);
        var observedDocumentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < clusters.Count; index++)
        {
            var cluster = clusters[index];
            if (cluster is null)
            {
                throw new ArgumentNullException(nameof(clusters), "Cluster entries cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(cluster.Id))
            {
                throw new ArgumentException("Cluster ids cannot be empty.", nameof(clusters));
            }

            if (cluster.Documents is null)
            {
                throw new ArgumentException("Cluster documents cannot be null.", nameof(clusters));
            }

            if (cluster.RecentOpenCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clusters), "Recent open count cannot be negative.");
            }

            foreach (var document in cluster.Documents)
            {
                if (document is null)
                {
                    throw new ArgumentNullException(nameof(clusters), "Document entries cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(document.Id))
                {
                    throw new ArgumentException("Document ids cannot be empty.", nameof(clusters));
                }

                if (string.IsNullOrWhiteSpace(document.DocumentType))
                {
                    throw new ArgumentException("Document types cannot be empty.", nameof(clusters));
                }

                if (!double.IsFinite(document.TextScore) || !double.IsFinite(document.VectorScore))
                {
                    throw new ArgumentException("Document scores must be finite numbers.", nameof(clusters));
                }

                if (document.TextScore < 0 || document.VectorScore < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(clusters), "Document scores cannot be negative.");
                }

                if (!observedDocumentIds.Add(document.Id))
                {
                    throw new ArgumentException($"Duplicate document id '{document.Id}'.", nameof(clusters));
                }
            }

            validatedClusters.Add(cluster);
        }

        return validatedClusters;
    }

    private static void ValidateRelativeSimilarityScores(IReadOnlyList<double> relativeSimilarityScores)
    {
        foreach (var value in relativeSimilarityScores)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentException("Relative similarity scores must be finite and non-negative.", nameof(relativeSimilarityScores));
            }
        }
    }

    private static string BuildExplanation(double baseScore, double coverageScore, double noteBoost, double interactionBoost, double finalScore)
    {
        return $"Base score {Math.Round(baseScore, 4)} comes from the document-score max/top-3 aggregation and {Math.Round(coverageScore, 4)} coverage. Note boost +{Math.Round(noteBoost, 4)} and interaction boost +{Math.Round(interactionBoost, 4)} produce a final score of {Math.Round(finalScore, 4)}.";
    }

    private static IList<double> Normalize(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<double>();
        }

        var min = values.Min();
        var max = values.Max();

        if (Math.Abs(max - min) < 0.0000001)
        {
            return values.Select(_ => 1.0).ToList();
        }

        return values.Select(value => (value - min) / (max - min)).ToList();
    }

    private static double NormalizeClusterScore(double clusterVectorScore, IReadOnlyList<double> allClusterVectorScores)
    {
        if (allClusterVectorScores.Count == 0)
        {
            return 0.0;
        }

        var min = allClusterVectorScores.Min();
        var max = allClusterVectorScores.Max();
        if (Math.Abs(max - min) < 0.0000001)
        {
            return 100.0;
        }

        return 100.0 * (clusterVectorScore - min) / (max - min);
    }
}

public sealed record HybridRankingOptions(
    double TextWeight,
    double VectorWeight,
    double AggregateMaxWeight,
    double AggregateTopThreeWeight,
    double AggregateCoverageWeight,
    double NoteBoost,
    double InteractionBoostCap)
{
    public static HybridRankingOptions Default { get; } = new(
        TextWeight: 0.7,
        VectorWeight: 0.3,
        AggregateMaxWeight: 0.65,
        AggregateTopThreeWeight: 0.25,
        AggregateCoverageWeight: 0.10,
        NoteBoost: 0.08,
        InteractionBoostCap: 0.05);
}

public sealed record HybridClusterCandidate(
    string Id,
    string Title,
    IReadOnlyList<HybridDocumentCandidate> Documents,
    int RecentOpenCount,
    bool HasMatchingNote);

public sealed record HybridDocumentCandidate(
    string Id,
    string DocumentType,
    double TextScore,
    double VectorScore,
    IReadOnlyList<string>? MatchedFields = null,
    string? Snippet = null);

public sealed record HybridClusterRankingResult(
    string ClusterId,
    string ClusterTitle,
    double Score,
    HybridScoreComponents ScoreComponents,
    IReadOnlyList<string> MatchedFields,
    string Explanation,
    IReadOnlyList<HybridSnippet> Snippets,
    double RelativeSimilarityPercent,
    string RelativeSimilarityLabel,
    string RelativeSimilarityTooltip);

public sealed record HybridScoreComponents(
    double MaxDocumentScore,
    double AverageTopThreeDocumentScore,
    double CoverageScore,
    double NoteBoost,
    double InteractionBoost,
    double BaseScore);

public sealed record HybridSnippet(string DocumentType, string Text, IReadOnlyList<string> MatchedFields);

internal sealed record HybridScoredDocument(
    string Id,
    string DocumentType,
    IReadOnlyList<string>? MatchedFields,
    string? Snippet,
    double TextScore,
    double VectorScore,
    double Score);
