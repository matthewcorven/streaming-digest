using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Stage 1 — transcript acquisition. Delegates to
/// <see cref="ITranscriptIngestionService"/>, which internally resolves captions vs
/// whisper (whisper readiness is enforced inside that service). A no-transcript
/// outcome is a warning, not a failure: segments/screenshots skip downstream and
/// the link-derived stages continue, per plan §5.2.
/// </summary>
public sealed class TranscriptStageHandler(
    ITranscriptIngestionService transcriptIngestion,
    IStreamingDigestDbContext dbContext,
    ILogger<TranscriptStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Transcript;

    public async Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        var result = await transcriptIngestion.IngestAsync(context.Video.Id, cancellationToken);

        if (result.Skipped)
        {
            context.Warnings.Add($"transcript skipped: {result.ErrorMessage ?? "not_long_form"}");
            return;
        }

        if (!result.Succeeded)
        {
            // TranscriptIngestionService already emitted the failure domain event and
            // updated video.TranscriptStatus (e.g. "unavailable_captions").
            context.Warnings.Add($"transcript unavailable: {result.ErrorMessage ?? "unknown"}");
            logger.LogInformation(
                "Video {VideoId}: transcript unavailable ({Error}); segments/screenshots will skip",
                context.Video.YoutubeVideoId, result.ErrorMessage);
            return;
        }

        context.Transcript = await dbContext.VideoTranscripts
            .Include(t => t.Cues)
            .FirstOrDefaultAsync(t => t.Id == result.TranscriptId, cancellationToken);
    }
}

/// <summary>
/// Stage 2 — segmentation. Prefers author chapters; otherwise deterministic
/// transcript chunking. LLM refinement is guarded by
/// <see cref="IModelReadinessGuard"/>: when the LLM capability is unready the
/// deterministic fallback is taken and a notification event is emitted exactly once.
/// </summary>
public sealed class SegmentsStageHandler(
    AuthorChapterSegmentationService authorChapterSegmentation,
    DeterministicTranscriptChunkingService deterministicChunking,
    IModelReadinessGuard modelReadinessGuard,
    ILogger<SegmentsStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Segments;

    public async Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Transcript is null)
        {
            // No transcript — nothing to segment; skipped stage, not an error.
            return;
        }

        var video = context.Video;
        var now = DateTimeOffset.UtcNow;
        SegmentGeneration? generation = null;
        var strategy = "none";

        if (!string.IsNullOrWhiteSpace(video.ChaptersJson))
        {
            generation = authorChapterSegmentation.CreateFromChaptersJson(video, generationVersion: 1, now: now);
            if (generation is not null)
            {
                strategy = "author_chapters";
            }
        }

        if (generation is null)
        {
            var llmReady = await modelReadinessGuard.IsReadyAsync(ModelCapabilities.Llm, cancellationToken);
            if (!llmReady)
            {
                context.Warnings.Add("segments: LLM unready; deterministic chunking fallback used");
                context.PendingEvents.Add(StageNotification.CapabilityUnready(
                    ModelCapabilities.Llm, StageName, "deterministic chunking fallback used", context));
            }

            generation = deterministicChunking.CreateFromTranscriptCues(
                video,
                context.Transcript.Cues.ToList(),
                windowSeconds: DeterministicTranscriptChunkingService.DefaultWindowSeconds,
                generationVersion: 1,
                now: now);
            if (generation is not null)
            {
                strategy = "deterministic_chunks";
            }
        }

        if (generation is null)
        {
            context.Warnings.Add("segments: no chapters and no usable transcript cues");
            return;
        }

        context.SegmentGeneration = generation;
        logger.LogDebug(
            "Video {VideoId}: created {Count} segments via {Strategy}",
            video.YoutubeVideoId, generation.Segments.Count, strategy);
    }
}
