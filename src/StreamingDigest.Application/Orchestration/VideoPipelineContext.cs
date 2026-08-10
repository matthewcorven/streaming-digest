using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Per-video working context carried through the ingestion pipeline stages.
/// </summary>
public sealed class VideoPipelineContext
{
    public required Video Video { get; init; }
    public required IngestionItem Item { get; init; }
    public required IngestionRun Run { get; init; }

    /// <summary>
    /// The transcript fetched by the transcript stage. <c>null</c> when the video
    /// has no transcript (segments/screenshots stages are skipped in that case).
    /// </summary>
    public VideoTranscript? Transcript { get; set; }

    /// <summary>Segment generation produced by the segments stage, if any.</summary>
    public SegmentGeneration? SegmentGeneration { get; set; }

    /// <summary>External resources created by the links stage for this video.</summary>
    public List<ExternalResource> Resources { get; } = [];

    /// <summary>Repository records persisted by the repos stage for this video.</summary>
    public List<RepositoryRecord> Repositories { get; } = [];

    /// <summary>Scraped pages persisted by the websites stage for this video.</summary>
    public List<ScrapedPage> ScrapedPages { get; } = [];

    /// <summary>
    /// Warning descriptions accumulated during this video's processing. A non-empty
    /// list at the end of the pipeline lands the item at
    /// <c>processed_with_warnings</c>.
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Domain events emitted by stage handlers; persisted by the pipeline.</summary>
    public List<DomainEvent> PendingEvents { get; } = [];

    /// <summary>
    /// Set by the pipeline when a stage throws unexpectedly: the item + video land at
    /// <c>failed</c> and no further stages run.
    /// </summary>
    public bool StageFailed { get; set; }
}
