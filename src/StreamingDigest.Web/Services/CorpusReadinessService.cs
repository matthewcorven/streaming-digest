using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Services;

public sealed class CorpusReadinessService
{
    private const string RunsEndpoint = "/api/internal/ingestion-runs?limit=5";
    private readonly SearchUiSessionService _searchUiSessionService;

    public CorpusReadinessService(SearchUiSessionService searchUiSessionService)
    {
        _searchUiSessionService = searchUiSessionService;
    }

    public async Task<CorpusReadinessState> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        var runs = await _searchUiSessionService.GetAuthenticatedJsonAsync<List<IngestionRunFixtureSummary>>(RunsEndpoint, cancellationToken)
            ?? [];

        if (runs.Count == 0)
        {
            return CorpusReadinessEvaluator.NoRuns();
        }

        var runDetails = new List<IngestionRunDetailViewModel>(runs.Count);
        foreach (var run in runs)
        {
            if (!Guid.TryParse(run.Id, out var runId))
            {
                continue;
            }

            var detail = await _searchUiSessionService.GetAuthenticatedJsonAsync<IngestionRunDetailViewModel>($"/api/internal/ingestion-runs/{runId}", cancellationToken);
            if (detail is not null)
            {
                runDetails.Add(detail);
            }
        }

        return CorpusReadinessEvaluator.Evaluate(runDetails);
    }
}

public static class CorpusReadinessEvaluator
{
    public static CorpusReadinessState NoRuns()
        => new(
            HasAnyRuns: false,
            HasSearchableCorpus: false,
            LatestRunFoundZeroVideos: false,
            WaitingHeadline: "Nothing to search yet",
            WaitingMessage: "Until the first ingestion run completes with at least one ingested video, search stays in a pre-corpus waiting state.",
            BackfillGuidance: string.Empty);

    public static CorpusReadinessState Evaluate(IReadOnlyList<IngestionRunDetailViewModel> runDetails)
    {
        ArgumentNullException.ThrowIfNull(runDetails);

        if (runDetails.Count == 0)
        {
            return NoRuns();
        }

        foreach (var detail in runDetails)
        {
            if (detail.FrozenOutcome.ProcessedVideos > 0)
            {
                return new CorpusReadinessState(
                    HasAnyRuns: true,
                    HasSearchableCorpus: true,
                    LatestRunFoundZeroVideos: false,
                    WaitingHeadline: string.Empty,
                    WaitingMessage: string.Empty,
                    BackfillGuidance: string.Empty);
            }
        }

        var latestRunFoundZeroVideos = runDetails[0].FrozenOutcome.ProcessedVideos == 0;
        return new CorpusReadinessState(
            HasAnyRuns: true,
            HasSearchableCorpus: false,
            LatestRunFoundZeroVideos: latestRunFoundZeroVideos,
            WaitingHeadline: "Corpus still warming up",
            WaitingMessage: "Search opens as soon as a completed run ingests at least one video.",
            BackfillGuidance: latestRunFoundZeroVideos
                ? "The latest completed run finished with zero ingested videos. Widen the backfill window or add another channel before returning to search."
                : string.Empty);
    }
}

public sealed record CorpusReadinessState(
    bool HasAnyRuns,
    bool HasSearchableCorpus,
    bool LatestRunFoundZeroVideos,
    string WaitingHeadline,
    string WaitingMessage,
    string BackfillGuidance);