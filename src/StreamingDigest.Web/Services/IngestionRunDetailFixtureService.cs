using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Services;

public interface IIngestionRunDetailFixtureService
{
    IngestionRunDetailViewModel GetRun(string runId);
    IReadOnlyList<IngestionRunFixtureSummary> GetFixtures();
}

public sealed class IngestionRunDetailFixtureService : IIngestionRunDetailFixtureService
{
    private readonly Dictionary<string, IngestionRunDetailViewModel> _fixtures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fixture-regular"] = CreateRegularFixture(),
        ["fixture-deferments"] = CreateDefermentFixture(),
        ["fixture-completed"] = CreateCompletedFixture(),
    };

    public IngestionRunDetailViewModel GetRun(string runId)
    {
        if (_fixtures.TryGetValue(runId, out var run))
        {
            return run;
        }

        throw new KeyNotFoundException($"No fixture was found for ingestion run '{runId}'.");
    }

    public IReadOnlyList<IngestionRunFixtureSummary> GetFixtures()
        => _fixtures.Values.Select(run => new IngestionRunFixtureSummary
        {
            Id = run.Id,
            Title = run.Title,
            Subtitle = run.Subtitle,
            StatusText = run.StatusText,
        }).ToList();

    private static IngestionRunDetailViewModel CreateRegularFixture() => new()
    {
        Id = "fixture-regular",
        Title = "Scheduled run #42",
        Subtitle = "Jul 17, 2026 6:00 AM • scheduled",
        StatusText = "Processed with warnings",
        Description = "Operational snapshot for a standard ingestion run with mixed outcomes and retry affordances.",
        FrozenOutcome = CreateOutcome("Frozen run outcome", "Run completed at 6:15 AM with one failed embedding stage.", 3, 5, 4, 1, 2, 3),
        LiveRollup = CreateOutcome("Live rollup", "Current item states reflect the latest recovered items.", 3, 5, 5, 0, 2, 3),
        Stages =
        [
            new IngestionRunStageViewModel { Name = "metadata", Completed = 5, Total = 5, Status = "done", Detail = "Captured metadata for each video." },
            new IngestionRunStageViewModel { Name = "transcript", Completed = 5, Total = 5, Status = "done", Detail = "Transcripts available for all videos." },
            new IngestionRunStageViewModel { Name = "whisper", Completed = 3, Total = 5, Status = "warning", Detail = "2 videos skipped due to absent captions." },
            new IngestionRunStageViewModel { Name = "embeddings", Completed = 4, Total = 5, Status = "warning", Detail = "1 video still needs a retry." },
            new IngestionRunStageViewModel { Name = "repository_metadata", Completed = 2, Total = 5, Status = "deferred", Detail = "GitHub host work paused for rate-limit backoff." },
        ],
        Items =
        [
            new IngestionRunItemViewModel
            {
                Id = "item-one",
                Title = "Video title one",
                Channel = "Tonbis AI Garage",
                Status = "processed",
                Stage = "metadata",
                TranscriptStatus = "ready",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "ready",
                LinkCount = 3,
                RepositoryCount = 1,
                WebsiteCount = 2,
            },
            new IngestionRunItemViewModel
            {
                Id = "item-two",
                Title = "Video title two",
                Channel = "Tonbis AI Garage",
                Status = "failed",
                CanRetry = true,
                FailureSummary = "Ollama timeout after 30s.",
                Stage = "embeddings",
                TranscriptStatus = "ready",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "failed",
                LinkCount = 4,
                RepositoryCount = 1,
                WebsiteCount = 2,
                RetryHistory =
                [
                    new IngestionRunRetryEventViewModel { Label = "Retry 1", Detail = "Automatic retry after the embedding timeout." },
                    new IngestionRunRetryEventViewModel { Label = "Retry 2", Detail = "Manual retry queued by the operator." },
                ],
            },
            new IngestionRunItemViewModel
            {
                Id = "item-three",
                Title = "Video title three",
                Channel = "Tonbis AI Garage",
                Status = "deferred",
                CanRetry = true,
                FailureSummary = "Whisper generation deferred while the rate-limit window is active.",
                Stage = "whisper",
                TranscriptStatus = "pending",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "pending",
                LinkCount = 2,
                RepositoryCount = 0,
                WebsiteCount = 1,
                RetryHistory =
                [
                    new IngestionRunRetryEventViewModel { Label = "Deferred", Detail = "Whisper work paused until the rate-limit window clears." },
                ],
            },
        ],
        Links =
        [
            new IngestionRunLinkViewModel { Label = "Hangfire", Url = "https://hangfire.local/ingestion/42" },
            new IngestionRunLinkViewModel { Label = "Logs", Url = "https://grafana.local/logs/42" },
            new IngestionRunLinkViewModel { Label = "Traces", Url = "https://grafana.local/traces/42" },
        ],
    };

    private static IngestionRunDetailViewModel CreateDefermentFixture() => new()
    {
        Id = "fixture-deferments",
        Title = "Backfill run #44",
        Subtitle = "Jul 18, 2026 2:13 PM • backfill",
        StatusText = "Deferred",
        Description = "A run that is still actively paused by rate-limit deferments and should surface them prominently.",
        DefermentBanner = "Repository and website processing is paused while the GitHub API cools down.",
        FrozenOutcome = CreateOutcome("Frozen run outcome", "The completed run reported two deferred items.", 2, 4, 2, 0, 1, 2),
        LiveRollup = CreateOutcome("Live rollup", "Current item status remains deferred until the pause clears.", 2, 4, 2, 0, 1, 2),
        Stages =
        [
            new IngestionRunStageViewModel { Name = "metadata", Completed = 4, Total = 4, Status = "done", Detail = "Metadata imported successfully." },
            new IngestionRunStageViewModel { Name = "transcript", Completed = 4, Total = 4, Status = "done", Detail = "Captions were available for all videos." },
            new IngestionRunStageViewModel { Name = "repository_metadata", Completed = 1, Total = 4, Status = "deferred", Detail = "Blocked by a rate-limit deferment for github.com." },
            new IngestionRunStageViewModel { Name = "website_scrape", Completed = 1, Total = 4, Status = "deferred", Detail = "Website extraction paused until the next retry window." },
        ],
        Items =
        [
            new IngestionRunItemViewModel
            {
                Id = "deferred-item-one",
                Title = "Video title four",
                Channel = "Cloud Native Weekly",
                Status = "deferred",
                CanRetry = true,
                FailureSummary = "Repository work is deferred until the GitHub pause expires.",
                Stage = "repository_metadata",
                TranscriptStatus = "ready",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "ready",
                LinkCount = 2,
                RepositoryCount = 1,
                WebsiteCount = 1,
                RetryHistory =
                [
                    new IngestionRunRetryEventViewModel { Label = "Paused", Detail = "Deferred because the current rate-limit window is still active." },
                ],
            },
            new IngestionRunItemViewModel
            {
                Id = "deferred-item-two",
                Title = "Video title five",
                Channel = "Cloud Native Weekly",
                Status = "deferred",
                CanRetry = true,
                FailureSummary = "Website scraping is deferred until the retry-at timestamp.",
                Stage = "website_scrape",
                TranscriptStatus = "ready",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "ready",
                LinkCount = 3,
                RepositoryCount = 0,
                WebsiteCount = 2,
            },
        ],
        Deferments =
        [
            new IngestionRunDefermentViewModel { Scope = "GitHub API", Reason = "Repository and website processing paused. Resumes at 7:15 AM.", ResumeLabel = "Resumes 7:15 AM", IsActive = true },
        ],
        Links =
        [
            new IngestionRunLinkViewModel { Label = "Hangfire", Url = "https://hangfire.local/ingestion/44" },
            new IngestionRunLinkViewModel { Label = "Logs", Url = "https://grafana.local/logs/44" },
            new IngestionRunLinkViewModel { Label = "Traces", Url = "https://grafana.local/traces/44" },
        ],
    };

    private static IngestionRunDetailViewModel CreateCompletedFixture() => new()
    {
        Id = "fixture-completed",
        Title = "Manual run #47",
        Subtitle = "Jul 19, 2026 10:10 AM • manual",
        StatusText = "Completed with warnings",
        Description = "A completed run that later recovered an item via retry, so the UI must show the frozen outcome alongside the current rollup.",
        IsCompleted = true,
        FrozenOutcome = CreateOutcome("Frozen run outcome", "The run completed with one failed embedding item.", 3, 5, 4, 1, 2, 3),
        LiveRollup = CreateOutcome("Live rollup", "The failed item was retried and now reports as processed.", 3, 5, 5, 0, 2, 3),
        Stages =
        [
            new IngestionRunStageViewModel { Name = "metadata", Completed = 5, Total = 5, Status = "done", Detail = "Metadata remains intact after the retry." },
            new IngestionRunStageViewModel { Name = "transcript", Completed = 5, Total = 5, Status = "done", Detail = "All transcripts were recovered successfully." },
            new IngestionRunStageViewModel { Name = "embeddings", Completed = 5, Total = 5, Status = "done", Detail = "The retried item completed successfully." },
        ],
        Items =
        [
            new IngestionRunItemViewModel
            {
                Id = "completed-item-one",
                Title = "Video title six",
                Channel = "Tonbis AI Garage",
                Status = "processed",
                Stage = "metadata",
                TranscriptStatus = "ready",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "ready",
                LinkCount = 2,
                RepositoryCount = 1,
                WebsiteCount = 1,
            },
            new IngestionRunItemViewModel
            {
                Id = "completed-item-two",
                Title = "Video title seven",
                Channel = "Tonbis AI Garage",
                Status = "processed",
                CanRetry = true,
                FailureSummary = "The run captured this as failed, but the current item state is processed after a retry.",
                Stage = "embeddings",
                TranscriptStatus = "ready",
                ScreenshotStatus = "ready",
                EmbeddingStatus = "ready",
                LinkCount = 3,
                RepositoryCount = 1,
                WebsiteCount = 2,
                RetryHistory =
                [
                    new IngestionRunRetryEventViewModel { Label = "Retry 1", Detail = "Recovered the embeddings stage after the original timeout." },
                    new IngestionRunRetryEventViewModel { Label = "Retry 2", Detail = "The item now reports as processed in the live rollup." },
                ],
            },
        ],
        Links =
        [
            new IngestionRunLinkViewModel { Label = "Hangfire", Url = "https://hangfire.local/ingestion/47" },
            new IngestionRunLinkViewModel { Label = "Logs", Url = "https://grafana.local/logs/47" },
            new IngestionRunLinkViewModel { Label = "Traces", Url = "https://grafana.local/traces/47" },
        ],
    };

    private static IngestionRunOutcomeViewModel CreateOutcome(string heading, string caption, int channels, int foundVideos, int processedVideos, int failedVideos, int repositories, int websites) => new()
    {
        Heading = heading,
        Caption = caption,
        Channels = channels,
        FoundVideos = foundVideos,
        ProcessedVideos = processedVideos,
        FailedVideos = failedVideos,
        Repositories = repositories,
        Websites = websites,
    };
}
