using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Contract for one per-video pipeline stage handler. Handlers never throw for
/// expected downstream failures: they record warnings / domain events on the
/// <see cref="VideoPipelineContext"/> and let the pipeline decide the item's
/// terminal status. Truly unexpected exceptions are captured by the pipeline and
/// fail the stage.
/// </summary>
public interface IVideoStageHandler
{
    /// <summary>The stage name (<see cref="IngestionStageNames"/>).</summary>
    string StageName { get; }

    Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Shared helpers for stage handlers: notification events (guard fallback /
/// capability unready) that must fire exactly once per occurrence.
/// </summary>
public static class StageNotification
{
    public static DomainEvent CapabilityUnready(
        string capability,
        string stageName,
        string action,
        VideoPipelineContext context)
        => new()
        {
            EventType = DomainEventTypeCatalog.ModelCapabilityUnready,
            Severity = "warning",
            EntityType = "video",
            EntityId = context.Video.Id,
            IngestionRunId = context.Run.Id,
            Message = $"Model capability '{capability}' unready during stage '{stageName}' for video '{context.Video.YoutubeVideoId}': {action}.",
        };

    public static DomainEvent FallbackApplied(
        string stageName,
        string fallback,
        VideoPipelineContext context)
        => new()
        {
            EventType = DomainEventTypeCatalog.StageFallbackApplied,
            Severity = "info",
            EntityType = "video",
            EntityId = context.Video.Id,
            IngestionRunId = context.Run.Id,
            Message = $"Stage '{stageName}' applied fallback '{fallback}' for video '{context.Video.YoutubeVideoId}'.",
        };
}
