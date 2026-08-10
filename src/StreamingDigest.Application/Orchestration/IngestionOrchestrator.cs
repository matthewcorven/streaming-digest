using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// A2 orchestrator (plan §5.1, ARCHITECTURE §4.1): resolves recent channel videos via the
/// active metadata adapter, filters by long-form + max-age, applies the idempotency guard,
/// persists the run + queued items, then executes the per-video guarded pipeline (§5.2)
/// with bounded concurrency and finalizes run counters + terminal status.
/// After the run reaches a terminal state, drives <see cref="IDigestAssemblyService"/> to
/// assemble + store the run-scoped Digest and enqueue the notification outbox (A8, §4.7).
/// </summary>
/// <remarks>
/// The orchestrator's own DbContext is used only for discovery / run bookkeeping. Each
/// video pipeline runs in a fresh DI scope so scoped services (DbContext, repositories)
/// are never shared across concurrent video pipelines.
/// </remarks>
public sealed class IngestionOrchestrator(
    IStreamingDigestDbContext db,
    IChannelRepository channels,
    IIngestionRunRepository runs,
    IIngestionItemRepository items,
    IMetadataAdapterSelector metadataAdapters,
    ApplicationConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    IDigestAssemblyService? digestAssemblyService = null,
    ILogger<IngestionOrchestrator>? logger = null) : IIngestionOrchestrator
{
    /// <inheritdoc />
    public async Task<IngestionRun> RunChannelIngestionAsync(
        ChannelIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = configuration.Ingestion;

        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = request.RunType,
            TriggeredBy = request.TriggeredBy,
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            OperationId = request.OperationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await runs.AddAsync(run, cancellationToken);

        try
        {
            var channel = await channels.GetByIdAsync(request.ChannelId, cancellationToken);
            if (channel is null)
            {
                return await FailRunAsync(run, $"Channel {request.ChannelId} not found.", cancellationToken);
            }

            run.ChannelsChecked = 1;
            if (channel.IsPaused)
            {
                return await CompleteRunAsync(run, "completed", $"Channel '{channel.YoutubeChannelId}' is paused; nothing to do.", cancellationToken);
            }

            var publishedAfter = VideoIngestionFilter.ComputePublishedAfterCutoff(
                DateTimeOffset.UtcNow, channel.DefaultMaxAgeDays, settings.DefaultMaxAgeDays);

            var candidates = await ResolveChannelVideosAsync(channel, publishedAfter, settings.MinDurationSeconds, cancellationToken);

            var newVideos = new List<Video>();
            var skippedCount = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.PublishedAt is { } published && published < publishedAfter)
                {
                    skippedCount++;
                    continue;
                }

                var existing = await db.Videos
                    .FirstOrDefaultAsync(v => v.YoutubeVideoId == candidate.YoutubeVideoId, cancellationToken);
                var skipReason = VideoIdempotencyService.ClassifySkipReason(existing?.IngestionStatus, request.IsReprocessRequest);
                if (skipReason != VideoSkipReason.None)
                {
                    skippedCount++;
                    continue;
                }

                var video = existing ?? candidate;
                video.IngestionStatus = IngestionStatuses.Processing;
                if (existing is null)
                {
                    await db.Videos.AddAsync(video, cancellationToken);
                }

                newVideos.Add(video);
            }

            await db.SaveChangesAsync(cancellationToken);

            run.NewVideosFound = newVideos.Count;
            run.VideosSkipped = skippedCount;

            var queuedItems = newVideos.Select(video => new IngestionItem
            {
                Id = Guid.NewGuid(),
                IngestionRunId = run.Id,
                OperationId = request.OperationId,
                ItemType = "video",
                ItemId = video.Id,
                ExternalKey = video.YoutubeVideoId,
                Stage = IngestionStageNames.Transcript,
                Status = "pending",
                Attempt = 0,
                MaxAttempts = 1,
                IsRetryable = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }).ToList();
            if (queuedItems.Count > 0)
            {
                await items.AddBulkAsync(queuedItems, cancellationToken);
            }

            var gate = new SemaphoreSlim(Math.Max(1, request.MaxVideoConcurrency));
            var pipelineTasks = newVideos.Zip(queuedItems, (video, item) => ProcessOneVideoAsync(run, video, item, gate, cancellationToken));
            var outcomes = await Task.WhenAll(pipelineTasks);

            run.VideosIngested = outcomes.Count(o => o.Outcome is VideoOutcome.Processed or VideoOutcome.ProcessedWithWarnings);
            run.VideosFailed = outcomes.Count(o => o.Outcome == VideoOutcome.Failed);
            run.TranscriptsFound = outcomes.Count(o => o.HasTranscript);
            run.TranscriptsMissing = outcomes.Count(o => o is { HasTranscript: false, Outcome: not VideoOutcome.Failed });
            run.RepositoriesFound = outcomes.Sum(o => o.RepositoryCount);

            var terminal = run.VideosFailed > 0
                ? "completed_with_warnings"
                : "completed";
            var completedRun = await CompleteRunAsync(run, terminal, null, cancellationToken);

            await AssembleDigestBestEffortAsync(completedRun, request, outcomes, newVideos, cancellationToken);

            return completedRun;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Ingestion run {RunId} failed", run.Id);
            return await FailRunAsync(run, ex.Message, cancellationToken);
        }
    }

    private enum VideoOutcome
    {
        Processed,
        ProcessedWithWarnings,
        Failed,
    }

    private sealed record VideoRunResult(VideoOutcome Outcome, bool HasTranscript, int RepositoryCount, Video Video)
    {
        public static VideoRunResult FailedResult(Video video) => new(VideoOutcome.Failed, false, 0, video);
    }

    private async Task<VideoRunResult> ProcessOneVideoAsync(
        IngestionRun run,
        Video video,
        IngestionItem item,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var videoScope = scopeFactory.CreateAsyncScope();
            var processor = videoScope.ServiceProvider.GetRequiredService<VideoPipelineProcessor>();
            var persistence = videoScope.ServiceProvider.GetRequiredService<IVideoPipelinePersistence>();

            var context = new VideoPipelineContext { Video = video, Item = item, Run = run };
            await processor.ProcessAsync(context, cancellationToken);

            var outcome = context.StageFailed
                ? VideoOutcome.Failed
                : context.Warnings.Count > 0 ? VideoOutcome.ProcessedWithWarnings : VideoOutcome.Processed;
            var videoStatus = outcome switch
            {
                VideoOutcome.Failed => IngestionStatuses.Failed,
                VideoOutcome.ProcessedWithWarnings => IngestionStatuses.ProcessedWithWarnings,
                _ => IngestionStatuses.Processed,
            };

            await persistence.FinalizeItemAsync(
                item.Id,
                videoStatus,
                context.Warnings.Count > 0 ? string.Join("; ", context.Warnings) : null,
                cancellationToken);
            await persistence.SetVideoIngestionStatusAsync(
                video.Id,
                videoStatus,
                run.Id,
                outcome != VideoOutcome.Failed,
                context.ScreenshotOutcome,
                cancellationToken);
            return new VideoRunResult(outcome, context.Transcript is not null, context.Repositories.Count, video);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Video pipeline failed for video {VideoId}", video.Id);
            await CompensateFailedVideoAsync(video, item, run, ex, cancellationToken);
            return VideoRunResult.FailedResult(video);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Best-effort compensating write when the pipeline throws: leaves the video + item at
    /// a terminal <c>failed</c> state instead of orphaned mid-state (video stuck at
    /// <c>processing</c> would be skipped by the idempotency guard forever). Runs in a
    /// fresh DI scope because the failed scope's DbContext may be poisoned.
    /// </summary>
    private async Task CompensateFailedVideoAsync(
        Video video,
        IngestionItem item,
        IngestionRun run,
        Exception failure,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IVideoPipelinePersistence>();
            await persistence.FinalizeItemAsync(item.Id, IngestionStatuses.Failed, failure.Message, cancellationToken);
            await persistence.SetVideoIngestionStatusAsync(
                video.Id,
                IngestionStatuses.Failed,
                run.Id,
                succeeded: false,
                ScreenshotStageOutcome.None,
                cancellationToken);
        }
        catch (Exception compensationError)
        {
            // Partial state after a secondary failure is acceptable only if logged loudly.
            logger?.LogError(
                compensationError,
                "Compensating failure write failed for video {VideoId} (item {ItemId}); row may be left mid-state",
                video.Id,
                item.Id);
        }
    }

    /// <summary>
    /// Resolves recent videos for the channel using the active metadata adapter.
    /// YouTube API path: list ids (max-age aware) then fetch each video's metadata.
    /// yt-dlp channel-feed discovery is not yet implemented (no existing runner seam).
    /// </summary>
    private async Task<IReadOnlyList<Video>> ResolveChannelVideosAsync(
        Channel channel,
        DateTimeOffset publishedAfter,
        int minDurationSeconds,
        CancellationToken cancellationToken)
    {
        var apiAdapter = metadataAdapters.YouTubeApiAdapter;
        if (apiAdapter is not null)
        {
            var list = await apiAdapter.ListChannelVideoIdsAsync(channel.YoutubeChannelId, publishedAfter, cancellationToken: cancellationToken);
            if (list.IsSuccess)
            {
                var videos = new List<Video>();
                foreach (var videoId in list.VideoIds)
                {
                    var fetched = await apiAdapter.FetchVideoAsync(videoId, channel.Id, minDurationSeconds, cancellationToken);
                    if (fetched.IsSuccess && fetched.Video is not null)
                    {
                        videos.Add(fetched.Video);
                    }
                }

                return videos;
            }

            logger?.LogWarning(
                "YouTube API channel listing failed for {ChannelId}: {Error}; no videos resolved",
                channel.YoutubeChannelId, list.ErrorMessage);
        }

        // yt-dlp channel-feed discovery is not yet implemented (no existing runner seam);
        // without the YouTube API adapter this run resolves no new videos. Tracked as a
        // follow-up — the pipeline below is adapter-agnostic once candidates exist.
        return [];
    }

    private async Task AssembleDigestBestEffortAsync(
        IngestionRun run,
        ChannelIngestionRequest request,
        VideoRunResult[] outcomes,
        List<Video> processedVideos,
        CancellationToken cancellationToken)
    {
        if (digestAssemblyService is null)
        {
            return;
        }

        try
        {
            var successVideos = outcomes
                .Where(o => o.Outcome is VideoOutcome.Processed or VideoOutcome.ProcessedWithWarnings)
                .Select(o => new DigestItem { Id = o.Video.YoutubeVideoId, Label = o.Video.DisplayTitle })
                .ToArray();

            var failedVideos = outcomes
                .Where(o => o.Outcome == VideoOutcome.Failed)
                .Select(o => new DigestItem { Id = o.Video.YoutubeVideoId, Label = o.Video.DisplayTitle })
                .ToArray();

            var digestRequest = new DigestAssemblyRequest
            {
                IngestionRunId = run.Id,
                OperationId = request.OperationId,
                RunType = run.RunType,
                NotificationTarget = request.NotificationTarget,
                NewVideos = successVideos,
                FailedItems = failedVideos,
                IsBackfillRun = string.Equals(run.RunType, "backfill", StringComparison.OrdinalIgnoreCase),
            };

            await digestAssemblyService.AssembleAndPersistAsync(digestRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Digest assembly is post-run best-effort; a failure must not retroactively
            // fail an otherwise successful ingestion run.
            logger?.LogError(ex, "Digest assembly failed for run {RunId}; run status is unchanged", run.Id);
        }
    }

    private async Task<IngestionRun> CompleteRunAsync(
        IngestionRun run,
        string status,
        string? summary,
        CancellationToken cancellationToken)
    {
        run.Status = status;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.SummaryJson = summary is null ? null : System.Text.Json.JsonSerializer.Serialize(new { note = summary });
        await runs.UpdateAsync(run, cancellationToken);
        return run;
    }

    private Task<IngestionRun> FailRunAsync(IngestionRun run, string error, CancellationToken cancellationToken)
        => CompleteRunAsync(run, "failed", error, cancellationToken);
}
