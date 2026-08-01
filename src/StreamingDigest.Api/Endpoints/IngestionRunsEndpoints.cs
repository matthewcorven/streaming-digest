using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Api.Endpoints;

internal static class IngestionRunsEndpoints
{
    public static void MapIngestionRunEndpoints(this WebApplication app)
    {
        app.MapGet("/api/internal/ingestion-runs", async (int? limit, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var take = Math.Clamp(limit ?? 25, 1, 200);
            var runs = await context.IngestionRuns
                .OrderByDescending(run => run.StartedAt)
                .Take(take)
                .ToListAsync(cancellationToken);

            var response = runs.Select(run => new IngestionRunFixtureSummary
            {
                Id = run.Id.ToString(),
                Title = CreateRunTitle(run),
                Subtitle = CreateRunSubtitle(run),
                StatusText = ToStatusText(run.Status, run.CompletedAt)
            }).ToList();

            return Results.Ok(response);
        });

        app.MapGet("/api/internal/ingestion-runs/{ingestionRunId:guid}", async (Guid ingestionRunId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var run = await context.IngestionRuns
                .Where(candidate => candidate.Id == ingestionRunId)
                .SingleOrDefaultAsync(cancellationToken);

            if (run is null)
            {
                return Results.NotFound();
            }

            var items = await context.IngestionItems
                .Where(item => item.IngestionRunId == ingestionRunId)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            var videoIds = items
                .Where(item => item.ItemId.HasValue && item.ItemType.Equals("video", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.ItemId!.Value)
                .Distinct()
                .ToList();

            var videos = await context.Videos
                .Where(video => videoIds.Contains(video.Id))
                .Select(video => new
                {
                    video.Id,
                    Title = video.Title,
                    ChannelName = video.Channel != null
                        ? (video.Channel.NameOverride ?? video.Channel.NameOriginal)
                        : "Unknown channel",
                    video.TranscriptStatus,
                    video.ScreenshotStatus,
                    video.IngestionStatus
                })
                .ToDictionaryAsync(video => video.Id, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var stageRollups = items
                .GroupBy(item => item.Stage)
                .Select(group =>
                {
                    var statuses = group.Select(item => item.Status?.ToLowerInvariant() ?? string.Empty).ToList();
                    var stageStatus = statuses.Any(status => status == "failed") ? "warning"
                        : statuses.Any(status => status == "deferred") ? "deferred"
                        : statuses.Any(status => status == "pending") ? "pending"
                        : "done";

                    return new IngestionRunStageViewModel
                    {
                        Name = group.Key,
                        Completed = group.Count(item => IsProcessedStatus(item.Status)),
                        Total = group.Count(),
                        Status = stageStatus,
                        Detail = $"{group.Count(item => item.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))} failed, {group.Count(item => item.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase))} deferred."
                    };
                })
                .OrderBy(stage => stage.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var perItemLinkCounts = items
                .Where(item => item.ItemType.Equals("link", StringComparison.OrdinalIgnoreCase) && item.ItemId.HasValue)
                .GroupBy(item => item.ItemId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());
            var perItemRepositoryCounts = items
                .Where(item => item.ItemType.StartsWith("repository", StringComparison.OrdinalIgnoreCase) && item.ItemId.HasValue)
                .GroupBy(item => item.ItemId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());
            var perItemWebsiteCounts = items
                .Where(item => (item.ItemType.StartsWith("website", StringComparison.OrdinalIgnoreCase) || item.ItemType.Equals("resource", StringComparison.OrdinalIgnoreCase)) && item.ItemId.HasValue)
                .GroupBy(item => item.ItemId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());

            var mappedItems = items
                .Where(item => item.ItemType is not ("link" or "repository" or "resource"))
                .Select(item =>
                {
                    var effectiveId = item.ItemId ?? item.Id;
                    videos.TryGetValue(item.ItemId ?? Guid.Empty, out var video);
                    var hasVideoData = video is not null;
                    var title = hasVideoData ? video!.Title : (!string.IsNullOrWhiteSpace(item.ExternalKey) ? item.ExternalKey : $"{item.ItemType} {effectiveId.ToString("N")[..8]}");
                    var itemStatus = ToStatusText(item.Status, item.CompletedAt);
                    var retryHistory = new List<IngestionRunRetryEventViewModel>();
                    if (item.RetryCount > 0)
                    {
                        retryHistory.Add(new IngestionRunRetryEventViewModel
                        {
                            Label = $"Retries: {item.RetryCount}",
                            Detail = $"Attempt {item.Attempt} of {item.MaxAttempts}"
                        });
                    }

                    return new IngestionRunItemViewModel
                    {
                        Id = effectiveId.ToString(),
                        Title = title,
                        Channel = hasVideoData ? video!.ChannelName : "Unknown channel",
                        Status = itemStatus.ToLowerInvariant(),
                        CanRetry = item.IsRetryable && item.Status is "failed" or "deferred",
                        FailureSummary = item.ErrorSummary,
                        Stage = item.Stage,
                        TranscriptStatus = hasVideoData ? video!.TranscriptStatus : "unknown",
                        ScreenshotStatus = hasVideoData ? video!.ScreenshotStatus : "unknown",
                        EmbeddingStatus = hasVideoData ? video!.IngestionStatus : "unknown",
                        LinkCount = perItemLinkCounts.GetValueOrDefault(effectiveId),
                        RepositoryCount = perItemRepositoryCounts.GetValueOrDefault(effectiveId),
                        WebsiteCount = perItemWebsiteCounts.GetValueOrDefault(effectiveId),
                        RetryHistory = retryHistory
                    };
                })
                .ToList();

            var deferments = items
                .Where(item => item.DeferredUntil.HasValue && (item.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase) || item.DeferredUntil > now))
                .Select(item => new IngestionRunDefermentViewModel
                {
                    Scope = string.IsNullOrWhiteSpace(item.ExternalKey) ? item.Stage : item.ExternalKey!,
                    Reason = string.IsNullOrWhiteSpace(item.DefermentReason) ? "Rate-limit deferment is active." : item.DefermentReason!,
                    ResumeLabel = item.DeferredUntil.HasValue ? $"Resumes {item.DeferredUntil.Value.ToLocalTime():h:mm tt}" : "Resume time unknown",
                    IsActive = item.DeferredUntil > now || item.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            var viewModel = new IngestionRunDetailViewModel
            {
                Id = run.Id.ToString(),
                Title = CreateRunTitle(run),
                Subtitle = CreateRunSubtitle(run),
                StatusText = ToStatusText(run.Status, run.CompletedAt),
                Description = $"Run type: {run.RunType}. Triggered by {run.TriggeredBy}.",
                IsCompleted = run.CompletedAt.HasValue,
                DefermentBanner = deferments.Count > 0 ? "One or more ingestion items are currently deferred." : null,
                FrozenOutcome = new IngestionRunOutcomeViewModel
                {
                    Heading = "Frozen run outcome",
                    Caption = run.CompletedAt.HasValue
                        ? $"Captured at {run.CompletedAt.Value.ToLocalTime():g}."
                        : "Run has not completed yet.",
                    Channels = run.ChannelsChecked,
                    FoundVideos = run.NewVideosFound,
                    ProcessedVideos = run.VideosIngested,
                    FailedVideos = run.VideosFailed,
                    Repositories = run.RepositoriesFound,
                    Websites = perItemWebsiteCounts.Values.Sum()
                },
                LiveRollup = new IngestionRunOutcomeViewModel
                {
                    Heading = "Live rollup",
                    Caption = "Derived from current ingestion item state.",
                    Channels = items.Count(item => item.ItemType.Equals("channel", StringComparison.OrdinalIgnoreCase)),
                    FoundVideos = items.Count(item => item.ItemType.Equals("video", StringComparison.OrdinalIgnoreCase)),
                    ProcessedVideos = items.Count(item => IsProcessedStatus(item.Status)),
                    FailedVideos = items.Count(item => item.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)),
                    Repositories = items.Count(item => item.ItemType.StartsWith("repository", StringComparison.OrdinalIgnoreCase)),
                    Websites = items.Count(item => item.ItemType.StartsWith("website", StringComparison.OrdinalIgnoreCase) || item.ItemType.Equals("resource", StringComparison.OrdinalIgnoreCase))
                },
                Stages = stageRollups,
                Items = mappedItems,
                Deferments = deferments,
                Links =
                [
                    new IngestionRunLinkViewModel
                    {
                        Label = "Hangfire",
                        Url = "/admin/jobs"
                    },
                    new IngestionRunLinkViewModel
                    {
                        Label = "Notifications",
                        Url = $"/api/internal/ingestion-runs/{ingestionRunId}/notifications"
                    }
                ]
            };

            return Results.Ok(viewModel);
        });

        app.MapGet("/api/internal/ingestion-runs/{ingestionRunId:guid}/notifications", async (Guid ingestionRunId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var notifications = await context.Notifications
                .Where(notification => notification.IngestionRunId == ingestionRunId)
                .OrderByDescending(notification => notification.CreatedAt)
                .Select(notification => new
                {
                    notification.Id,
                    notification.OperationId,
                    notification.Provider,
                    notification.Target,
                    notification.Status,
                    notification.AttemptCount,
                    notification.NextRetryAt,
                    notification.ProviderMessageId,
                    notification.ErrorSummary,
                    notification.SentAt,
                    notification.CreatedAt,
                    notification.UpdatedAt,
                    Retryable = notification.Status == "pending" || notification.Status == "failed"
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(notifications);
        });
    }

    private static string CreateRunTitle(IngestionRun run)
    {
        var runType = string.IsNullOrWhiteSpace(run.RunType) ? "Ingestion" : char.ToUpperInvariant(run.RunType[0]) + run.RunType[1..];
        return $"{runType} run #{run.Id.ToString("N")[..8]}";
    }

    private static string CreateRunSubtitle(IngestionRun run)
        => $"{run.StartedAt.ToLocalTime():MMM d, yyyy h:mm tt} • {run.RunType}";

    private static string ToStatusText(string status, DateTimeOffset? completedAt)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant() ?? "unknown";
        if (normalizedStatus is "completed" or "done" && completedAt is not null)
        {
            return "Completed";
        }

        return normalizedStatus switch
        {
            "failed" => "Failed",
            "deferred" => "Deferred",
            "pending" => "Pending",
            "in_progress" => "In progress",
            "completed_with_warnings" => "Completed with warnings",
            "processed_with_warnings" => "Processed with warnings",
            _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status
        };
    }

    private static bool IsProcessedStatus(string status)
    {
        var normalized = status?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized is "processed" or "done" or "completed" or "processed_with_warnings" or "completed_with_warnings";
    }
}