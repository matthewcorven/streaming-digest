using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Screenshots;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Stage 3 — screenshots. Resolves a local media file (via yt-dlp through
/// <see cref="IVideoMediaSourceResolver"/>) and generates one WebP screenshot per
/// segment. When media cannot be resolved the stage defers with a warning rather
/// than failing, per plan §5.2. Individual segment failures degrade the stage to a
/// warning, not a video failure.
/// </summary>
public sealed class ScreenshotsStageHandler(
    IScreenshotGenerationService screenshotGeneration,
    IVideoMediaSourceResolver? mediaSourceResolver,
    ILogger<ScreenshotsStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Screenshots;

    public async Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        if (context.SegmentGeneration is null || context.SegmentGeneration.Segments.Count == 0)
        {
            // Nothing to screenshot (skipped, not an error).
            return;
        }

        ResolvedMediaFile? media = null;
        if (mediaSourceResolver is not null)
        {
            try
            {
                media = await mediaSourceResolver.ResolveAsync(context.Video.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex,
                    "Video {VideoId}: media resolution failed; screenshots deferred",
                    context.Video.YoutubeVideoId);
            }
        }

        if (media is null)
        {
            context.Warnings.Add("screenshots: no local media file available; deferred");
            context.ScreenshotOutcome = ScreenshotStageOutcome.Deferred;
            context.PendingEvents.Add(new DomainEvent
            {
                EventType = DomainEventTypeCatalog.ScreenshotFileMissing,
                Severity = "warning",
                EntityType = "video",
                EntityId = context.Video.Id,
                IngestionRunId = context.Run.Id,
                Message = $"No local media file for video '{context.Video.YoutubeVideoId}'; screenshot generation deferred.",
            });
            return;
        }

        try
        {
            var failures = 0;
            foreach (var segment in context.SegmentGeneration.Segments)
            {
                var outputPath = Path.Combine(
                    Path.GetTempPath(),
                    "streaming-digest",
                    "screenshots",
                    $"{segment.Id}.webp");

                var result = await screenshotGeneration.GenerateAsync(
                    new ScreenshotGenerationRequest(
                        media.FilePath,
                        outputPath,
                        OffsetSeconds: (double)segment.StartSeconds),
                    cancellationToken);

                if (result.Succeeded)
                {
                    context.SegmentGeneration.Screenshots.Add(new SegmentScreenshot
                    {
                        VideoId = context.Video.Id,
                        SegmentGenerationId = context.SegmentGeneration.Id,
                        SegmentId = segment.Id,
                        TimestampSeconds = segment.StartSeconds,
                        FilePath = outputPath,
                        IsActive = true,
                    });
                }
                else
                {
                    failures++;
                    logger.LogDebug(
                        "Video {VideoId} segment {SegmentId}: screenshot failed: {Error}",
                        context.Video.YoutubeVideoId, segment.Id, result.ErrorMessage);
                }
            }

            if (failures > 0)
            {
                context.Warnings.Add($"screenshots: {failures} of {context.SegmentGeneration.Segments.Count} segments failed");
                context.ScreenshotOutcome = ScreenshotStageOutcome.PartialFailure;
            }
            else if (context.SegmentGeneration.Screenshots.Count > 0)
            {
                context.ScreenshotOutcome = ScreenshotStageOutcome.Generated;
            }
        }
        finally
        {
            if (media.DeleteWhenFinished && File.Exists(media.FilePath))
            {
                File.Delete(media.FilePath);
            }
        }
    }
}
