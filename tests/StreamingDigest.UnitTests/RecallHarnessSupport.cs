using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Application;
using StreamingDigest.UnitTests.Fixtures;

namespace StreamingDigest.UnitTests;

internal sealed class RecallGoldenDataset
{
    [JsonPropertyName("dataset_version")]
    public string DatasetVersion { get; set; } = string.Empty;

    [JsonPropertyName("authorship_rule")]
    public string AuthorshipRule { get; set; } = string.Empty;

    [JsonPropertyName("queries")]
    public List<RecallGoldenQuery> Queries { get; set; } = [];
}

internal sealed class RecallGoldenQuery
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("expected_cluster_id")]
    public string ExpectedClusterId { get; set; } = string.Empty;

    [JsonPropertyName("expected_video_title")]
    public string ExpectedVideoTitle { get; set; } = string.Empty;

    [JsonPropertyName("provenance_notes")]
    public string ProvenanceNotes { get; set; } = string.Empty;
}

internal sealed class HighSignalCalibrationMetadata
{
    [JsonPropertyName("Provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("Model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("Dimensions")]
    public int Dimensions { get; set; }

    [JsonPropertyName("RecommendedThreshold")]
    public CalibrationThreshold RecommendedThreshold { get; set; } = new();
}

internal sealed class CalibrationThreshold
{
    [JsonPropertyName("ThresholdPercent")]
    public int ThresholdPercent { get; set; }
}

internal static class RecallHarnessSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static RecallGoldenDataset LoadDataset()
    {
        var fixtures = new FixtureLoader();
        return JsonSerializer.Deserialize<RecallGoldenDataset>(fixtures.ReadText("recall/vague-query-corpus.json"))!;
    }

    public static HighSignalCalibrationMetadata LoadCalibration()
    {
        var path = ResolveRepoFile("docs/verification/12.x-high-signal-threshold-calibration.json");
        return JsonSerializer.Deserialize<HighSignalCalibrationMetadata>(File.ReadAllText(path))!;
    }

    public static SearchRecallHarnessReport BuildPassingReport()
    {
        return BuildReport(SearchRecallRepresentativeCorpusFactory.CreateRepresentativeCorpus());
    }

    public static SearchRecallHarnessReport BuildReport(IReadOnlyList<SearchCorpusClusterSeed> corpus)
    {
        var dataset = LoadDataset();
        var calibration = LoadCalibration();
        var service = new SearchUiService(corpus);
        var queries = dataset.Queries
            .Select(query => EvaluateQuery(service, query))
            .ToList();

        return new SearchRecallHarnessReport
        {
            Task = "12.7-search-recall-harness",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            DatasetVersion = dataset.DatasetVersion,
            AuthorshipRule = dataset.AuthorshipRule,
            RankingFormulaVersion = HybridRankingService.FormulaVersion,
            CandidateScoringVersion = SearchUiService.CandidateScoringVersion,
            DocumentConstructionVersion = SearchDocumentGenerator.DocumentConstructionVersion,
            EmbeddingProvider = calibration.Provider,
            EmbeddingModel = calibration.Model,
            EmbeddingDimensions = calibration.Dimensions,
            HighSignalThresholdPercent = calibration.RecommendedThreshold.ThresholdPercent,
            CorpusVideoCount = corpus.Count,
            DistractorVideoCount = corpus.Count - SearchUiCorpusCatalog.CreateDefaultFixtureCorpus().Count,
            QueryCount = queries.Count,
            PassedQueryCount = queries.Count(result => result.PassedTop3Recall),
            Queries = queries
        };
    }

    public static string ToJson(SearchRecallHarnessReport report)
    {
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ToMarkdown(SearchRecallHarnessReport report)
    {
        var outcome = report.PassedQueryCount == report.QueryCount
            ? $"The recall harness held **{report.PassedQueryCount}/{report.QueryCount}** queries at **top-3 of {report.CorpusVideoCount} videos**."
            : $"The recall harness regressed: **{report.PassedQueryCount}/{report.QueryCount}** queries stayed in the top 3.";

        var lines = new List<string>
        {
            "# Verification: 12.7 — Search recall evaluation harness",
            string.Empty,
            "> Append-only evidence. Each run adds a dated entry; prior entries are never overwritten.",
            string.Empty,
            "---",
            string.Empty,
            $"## Run 1 — {report.GeneratedAtUtc:yyyy-MM-dd}",
            string.Empty,
            "### Outcome",
            string.Empty,
            outcome,
            string.Empty,
            $"- **Corpus:** `{report.CorpusVideoCount}` videos (`{report.DistractorVideoCount}` deterministic distractors + `{report.CorpusVideoCount - report.DistractorVideoCount}` golden fixtures)",
            $"- **Queries:** `{report.QueryCount}`",
            $"- **Ranking formula version:** `{report.RankingFormulaVersion}`",
            $"- **Candidate scoring version:** `{report.CandidateScoringVersion}`",
            $"- **Document construction version:** `{report.DocumentConstructionVersion}`",
            $"- **Embedding provider/model/dimensions:** `{report.EmbeddingProvider}` / `{report.EmbeddingModel}` / `{report.EmbeddingDimensions}`",
            $"- **High-signal threshold reference:** `{report.HighSignalThresholdPercent}%` (`docs/verification/12.x-high-signal-threshold-calibration.json`)",
            string.Empty,
            $"Machine-readable evidence: `12.7-search-recall-harness.json` (same directory, `Task: {report.Task}`).",
            string.Empty,
            "### Dataset rule",
            string.Empty,
            report.AuthorshipRule,
            string.Empty,
            "### Verification command",
            string.Empty,
            "```bash",
            "dotnet test tests/StreamingDigest.UnitTests/StreamingDigest.UnitTests.csproj --filter SearchRecallHarnessTests",
            "```",
            string.Empty,
            "### Per-query results",
            string.Empty,
            "| query id | expected cluster | rank | top-3 | score | base | max doc | top-3 avg | coverage | note | interaction |",
            "|---|---|---:|:---:|---:|---:|---:|---:|---:|---:|---:|"
        };

        foreach (var query in report.Queries)
        {
            lines.Add(
                $"| `{query.Id}` | `{query.ExpectedClusterId}` | {query.ExpectedClusterRank?.ToString() ?? "miss"} | {(query.PassedTop3Recall ? "yes" : "no")} | {FormatScore(query.ExpectedClusterScore)} | {FormatScore(query.ExpectedScoreComponents?.BaseScore)} | {FormatScore(query.ExpectedScoreComponents?.MaxDocumentScore)} | {FormatScore(query.ExpectedScoreComponents?.AverageTopThreeDocumentScore)} | {FormatScore(query.ExpectedScoreComponents?.CoverageScore)} | {FormatScore(query.ExpectedScoreComponents?.NoteBoost)} | {FormatScore(query.ExpectedScoreComponents?.InteractionBoost)} |");
        }

        lines.Add(string.Empty);
        lines.Add("### Query provenance");
        lines.Add(string.Empty);

        foreach (var query in report.Queries)
        {
            lines.Add($"- **{query.Id}** — {query.Query} — {query.ProvenanceNotes}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static string ResolveRepoFile(string relativePath)
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            var candidate = Path.Combine(currentDirectory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            currentDirectory = currentDirectory.Parent;
        }

        var workingCandidate = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        if (File.Exists(workingCandidate))
        {
            return workingCandidate;
        }

        throw new FileNotFoundException($"Could not resolve repository file '{relativePath}'.", relativePath);
    }

    public static void AssertJsonMatches(string expectedJsonPath, string actualJson)
    {
        var expected = JsonSerializer.Deserialize<SearchRecallHarnessReport>(File.ReadAllText(expectedJsonPath), JsonOptions)!;
        var actual = JsonSerializer.Deserialize<SearchRecallHarnessReport>(actualJson, JsonOptions)!;

        Assert.Equal(expected.Task, actual.Task);
        Assert.Equal(expected.DatasetVersion, actual.DatasetVersion);
        Assert.Equal(expected.AuthorshipRule, actual.AuthorshipRule);
        Assert.Equal(expected.RankingFormulaVersion, actual.RankingFormulaVersion);
        Assert.Equal(expected.CandidateScoringVersion, actual.CandidateScoringVersion);
        Assert.Equal(expected.DocumentConstructionVersion, actual.DocumentConstructionVersion);
        Assert.Equal(expected.EmbeddingProvider, actual.EmbeddingProvider);
        Assert.Equal(expected.EmbeddingModel, actual.EmbeddingModel);
        Assert.Equal(expected.EmbeddingDimensions, actual.EmbeddingDimensions);
        Assert.Equal(expected.HighSignalThresholdPercent, actual.HighSignalThresholdPercent);
        Assert.Equal(expected.CorpusVideoCount, actual.CorpusVideoCount);
        Assert.Equal(expected.DistractorVideoCount, actual.DistractorVideoCount);
        Assert.Equal(expected.QueryCount, actual.QueryCount);
        Assert.Equal(expected.PassedQueryCount, actual.PassedQueryCount);
        Assert.Equal(expected.Queries.Count, actual.Queries.Count);

        for (var index = 0; index < expected.Queries.Count; index++)
        {
            var expectedQuery = expected.Queries[index];
            var actualQuery = actual.Queries[index];
            Assert.Equal(expectedQuery.Id, actualQuery.Id);
            Assert.Equal(expectedQuery.Query, actualQuery.Query);
            Assert.Equal(expectedQuery.ExpectedClusterId, actualQuery.ExpectedClusterId);
            Assert.Equal(expectedQuery.ExpectedVideoTitle, actualQuery.ExpectedVideoTitle);
            Assert.Equal(expectedQuery.ProvenanceNotes, actualQuery.ProvenanceNotes);
            Assert.Equal(expectedQuery.ExpectedClusterRank, actualQuery.ExpectedClusterRank);
            Assert.Equal(expectedQuery.PassedTop3Recall, actualQuery.PassedTop3Recall);
            Assert.Equal(expectedQuery.ExpectedClusterScore, actualQuery.ExpectedClusterScore);
            AssertScoreComponents(expectedQuery.ExpectedScoreComponents, actualQuery.ExpectedScoreComponents);
            Assert.Equal(expectedQuery.TopThree.Count, actualQuery.TopThree.Count);

            for (var topIndex = 0; topIndex < expectedQuery.TopThree.Count; topIndex++)
            {
                var expectedTop = expectedQuery.TopThree[topIndex];
                var actualTop = actualQuery.TopThree[topIndex];
                Assert.Equal(expectedTop.ClusterId, actualTop.ClusterId);
                Assert.Equal(expectedTop.Title, actualTop.Title);
                Assert.Equal(expectedTop.Score, actualTop.Score);
                Assert.Equal(expectedTop.RelativeSimilarityPercent, actualTop.RelativeSimilarityPercent);
                AssertScoreComponents(expectedTop.ScoreComponents, actualTop.ScoreComponents);
            }
        }
    }

    private static void AssertScoreComponents(SearchScoreComponentsResponse? expected, SearchScoreComponentsResponse? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected, actual);
            return;
        }

        Assert.Equal(expected.BaseScore, actual.BaseScore);
        Assert.Equal(expected.MaxDocumentScore, actual.MaxDocumentScore);
        Assert.Equal(expected.AverageTopThreeDocumentScore, actual.AverageTopThreeDocumentScore);
        Assert.Equal(expected.CoverageScore, actual.CoverageScore);
        Assert.Equal(expected.NoteBoost, actual.NoteBoost);
        Assert.Equal(expected.InteractionBoost, actual.InteractionBoost);
    }

    private static SearchRecallQueryResult EvaluateQuery(SearchUiService service, RecallGoldenQuery query)
    {
        var response = service.Search(new SearchRequest
        {
            Query = query.Query,
            Filters = new SearchFilters
            {
                ResultType = "video"
            }
        });

        var rankedResults = response.Results.ToList();
        var expectedIndex = rankedResults.FindIndex(result => string.Equals(result.ClusterId, query.ExpectedClusterId, StringComparison.OrdinalIgnoreCase));
        var expectedResult = expectedIndex >= 0 ? rankedResults[expectedIndex] : null;

        return new SearchRecallQueryResult
        {
            Id = query.Id,
            Query = query.Query,
            ExpectedClusterId = query.ExpectedClusterId,
            ExpectedVideoTitle = query.ExpectedVideoTitle,
            ProvenanceNotes = query.ProvenanceNotes,
            ExpectedClusterRank = expectedIndex >= 0 ? expectedIndex + 1 : null,
            PassedTop3Recall = expectedIndex is >= 0 and < 3,
            ExpectedClusterScore = expectedResult?.Score,
            ExpectedScoreComponents = expectedResult?.ScoreComponents,
            TopThree = rankedResults
                .Take(3)
                .Select(result => new SearchRecallTopResult
                {
                    ClusterId = result.ClusterId,
                    Title = result.Title,
                    Score = result.Score,
                    RelativeSimilarityPercent = result.RelativeSimilarityPercent,
                    ScoreComponents = result.ScoreComponents
                })
                .ToList()
        };
    }

    private static string FormatScore(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.0000") : "n/a";
    }
}

internal sealed class SearchRecallHarnessReport
{
    [JsonPropertyName("task")]
    public string Task { get; set; } = string.Empty;

    [JsonPropertyName("generated_at_utc")]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [JsonPropertyName("dataset_version")]
    public string DatasetVersion { get; set; } = string.Empty;

    [JsonPropertyName("authorship_rule")]
    public string AuthorshipRule { get; set; } = string.Empty;

    [JsonPropertyName("ranking_formula_version")]
    public string RankingFormulaVersion { get; set; } = string.Empty;

    [JsonPropertyName("candidate_scoring_version")]
    public string CandidateScoringVersion { get; set; } = string.Empty;

    [JsonPropertyName("document_construction_version")]
    public string DocumentConstructionVersion { get; set; } = string.Empty;

    [JsonPropertyName("embedding_provider")]
    public string EmbeddingProvider { get; set; } = string.Empty;

    [JsonPropertyName("embedding_model")]
    public string EmbeddingModel { get; set; } = string.Empty;

    [JsonPropertyName("embedding_dimensions")]
    public int EmbeddingDimensions { get; set; }

    [JsonPropertyName("high_signal_threshold_percent")]
    public int HighSignalThresholdPercent { get; set; }

    [JsonPropertyName("corpus_video_count")]
    public int CorpusVideoCount { get; set; }

    [JsonPropertyName("distractor_video_count")]
    public int DistractorVideoCount { get; set; }

    [JsonPropertyName("query_count")]
    public int QueryCount { get; set; }

    [JsonPropertyName("passed_query_count")]
    public int PassedQueryCount { get; set; }

    [JsonPropertyName("queries")]
    public List<SearchRecallQueryResult> Queries { get; set; } = [];
}

internal sealed class SearchRecallQueryResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("expected_cluster_id")]
    public string ExpectedClusterId { get; set; } = string.Empty;

    [JsonPropertyName("expected_video_title")]
    public string ExpectedVideoTitle { get; set; } = string.Empty;

    [JsonPropertyName("provenance_notes")]
    public string ProvenanceNotes { get; set; } = string.Empty;

    [JsonPropertyName("expected_cluster_rank")]
    public int? ExpectedClusterRank { get; set; }

    [JsonPropertyName("passed_top3_recall")]
    public bool PassedTop3Recall { get; set; }

    [JsonPropertyName("expected_cluster_score")]
    public double? ExpectedClusterScore { get; set; }

    [JsonPropertyName("expected_score_components")]
    public SearchScoreComponentsResponse? ExpectedScoreComponents { get; set; }

    [JsonPropertyName("top_three")]
    public List<SearchRecallTopResult> TopThree { get; set; } = [];
}

internal sealed class SearchRecallTopResult
{
    [JsonPropertyName("cluster_id")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("relative_similarity_percent")]
    public double RelativeSimilarityPercent { get; set; }

    [JsonPropertyName("score_components")]
    public SearchScoreComponentsResponse ScoreComponents { get; set; } = new();
}
