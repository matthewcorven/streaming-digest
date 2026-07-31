using System.Runtime.InteropServices;
using System.Text.Json;
using StreamingDigest.Application;

var report = SearchLatencyBenchmark.RunRepresentativeBenchmark();
var repoRoot = ResolveRepoRoot();
var outputDirectory = Path.Combine(repoRoot, "docs", "verification");
Directory.CreateDirectory(outputDirectory);

var environment = new
{
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    dotnet_sdk = Environment.Version.ToString(),
    processor_count = Environment.ProcessorCount
};

var payload = new
{
    task = report.Task,
    generated_at_utc = report.GeneratedAtUtc,
    ranking_formula_version = report.RankingFormulaVersion,
    candidate_scoring_version = report.CandidateScoringVersion,
    document_construction_version = report.DocumentConstructionVersion,
    progress_indicator_delay_ms = report.ProgressIndicatorDelayMs,
    environment,
    corpora = report.Corpora
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};

var jsonPath = Path.Combine(outputDirectory, "12.8-search-latency-baseline.json");
await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, jsonOptions));

var markdownPath = Path.Combine(outputDirectory, "12.8-search-latency-baseline.md");
await File.WriteAllTextAsync(markdownPath, ToMarkdown(report, environment));

Console.WriteLine($"Wrote {jsonPath}");
Console.WriteLine($"Wrote {markdownPath}");

static string ResolveRepoRoot()
{
    var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (currentDirectory is not null)
    {
        if (Directory.Exists(Path.Combine(currentDirectory.FullName, "docs", "verification")))
        {
            return currentDirectory.FullName;
        }

        currentDirectory = currentDirectory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find the repository root containing docs/verification.");
}

static string ToMarkdown(SearchLatencyBenchmarkSuite report, object environment)
{
    var lines = new List<string>
    {
        "# Verification: 12.8 — Search latency benchmark baseline",
        string.Empty,
        "> Append-only evidence. Each run adds a dated entry; prior entries are never overwritten.",
        string.Empty,
        "---",
        string.Empty,
        $"## Run 1 — {report.GeneratedAtUtc:yyyy-MM-dd}",
        string.Empty,
        "### Outcome",
        string.Empty
    };

    if (report.Corpora.All(corpus => corpus.MeetsLatencyTarget))
    {
        lines.Add("Both representative corpora stayed inside the Task 12.8 latency targets.");
    }
    else
    {
        lines.Add("At least one representative corpus missed the Task 12.8 latency targets. See the remediation plan below.");
    }

    lines.Add(string.Empty);
    lines.Add("| corpus | videos | search docs | embeddings | p50 (ms) | target | p95 (ms) | target | verdict |");
    lines.Add("|---|---:|---:|---:|---:|---:|---:|---:|---|");

    foreach (var corpus in report.Corpora)
    {
        lines.Add(
            $"| `{corpus.VideoCount}` videos | {corpus.VideoCount} | {corpus.EntityCounts.SearchDocuments} | {corpus.EntityCounts.Embeddings} | {corpus.P50Ms:0.000} | ≤ {corpus.TargetP50Ms:0} | {corpus.P95Ms:0.000} | ≤ {corpus.TargetP95Ms:0} | {(corpus.MeetsLatencyTarget ? "met" : "missed")} |");
    }

    lines.Add(string.Empty);
    lines.Add("### Measurement environment");
    lines.Add(string.Empty);
    lines.Add($"- `{JsonSerializer.Serialize(environment)}`");
    lines.Add($"- Ranking formula: `{report.RankingFormulaVersion}`");
    lines.Add($"- Candidate scoring: `{report.CandidateScoringVersion}`");
    lines.Add($"- Document construction: `{report.DocumentConstructionVersion}`");
    lines.Add($"- UI delayed progress threshold: `{report.ProgressIndicatorDelayMs} ms`");
    lines.Add(string.Empty);
    lines.Add("### Dataset composition");
    lines.Add(string.Empty);
    lines.Add("| corpus | segments | transcript clusters | links | repository links | note clusters | queries | measurements |");
    lines.Add("|---|---:|---:|---:|---:|---:|---:|---:|");

    foreach (var corpus in report.Corpora)
    {
        lines.Add(
            $"| `{corpus.VideoCount}` videos | {corpus.EntityCounts.Segments} | {corpus.EntityCounts.TranscriptClusters} | {corpus.EntityCounts.Links} | {corpus.EntityCounts.RepositoryLinks} | {corpus.EntityCounts.NoteClusters} | {corpus.QueryCount} | {corpus.MeasurementCount} |");
    }

    lines.Add(string.Empty);
    lines.Add("The 500- and 2,000-video corpora both come from `SearchRecallRepresentativeCorpusFactory`, so the same deterministic distractor builder from ADR-0013 drives recall and latency measurement.");
    lines.Add(string.Empty);
    lines.Add("### Sample queries");
    lines.Add(string.Empty);

    foreach (var corpus in report.Corpora)
    {
        lines.Add($"- **{corpus.VideoCount} videos:** {string.Join(" | ", corpus.SampleQueries)}");
    }

    lines.Add(string.Empty);
    lines.Add("### Re-run command");
    lines.Add(string.Empty);
    lines.Add("```bash");
    lines.Add("dotnet run --project spikes/StreamingDigest.SearchLatencyPrototype");
    lines.Add("```");
    lines.Add(string.Empty);
    lines.Add("### UI progress-state requirement");
    lines.Add(string.Empty);
    lines.Add("The search page now keeps the button in its immediate `Searching…` state and reveals a separate progress card/spinner only after 1 second, so fast queries avoid extra UI churn while slow queries surface visible progress.");
    lines.Add(string.Empty);

    if (report.Corpora.Any(corpus => !corpus.MeetsLatencyTarget))
    {
        lines.Add("### Remediation plan");
        lines.Add(string.Empty);
        lines.Add("1. Keep the delayed progress card at 1 second so slow queries remain visible.");
        lines.Add("2. Profile the slow corpus with the benchmark's sample queries to identify the hottest matching paths.");
        lines.Add("3. Re-run the baseline after the next ranking or corpus-shape optimization.");
        lines.Add(string.Empty);
    }

    lines.Add("Machine-readable evidence: `12.8-search-latency-baseline.json`.");
    lines.Add(string.Empty);
    return string.Join(Environment.NewLine, lines);
}
