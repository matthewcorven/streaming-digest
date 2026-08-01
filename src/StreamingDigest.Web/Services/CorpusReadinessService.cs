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
            WaitingHeadline: "Start by adding a channel",
            WaitingMessage: "Add a channel to begin building the corpus. Search, the dashboard, and run history will appear after the first videos become available.",
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
            WaitingHeadline: "Add another channel to keep building the corpus",
            WaitingMessage: "This workspace stays in its empty-state experience until at least one channel contributes searchable videos.",
            BackfillGuidance: string.Empty);
    }
}

public sealed record CorpusReadinessState(
    bool HasAnyRuns,
    bool HasSearchableCorpus,
    bool LatestRunFoundZeroVideos,
    string WaitingHeadline,
    string WaitingMessage,
    string BackfillGuidance);