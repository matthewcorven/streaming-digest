namespace StreamingDigest.Domain;

public sealed class SegmentGeneration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VideoId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public int GenerationVersion { get; init; }
    public bool IsActive { get; set; }
    public bool RequiresUserApproval { get; set; }
    public string Status { get; set; } = "draft";
    public string? LlmModel { get; set; }
    public string? LlmPromptVersion { get; set; }
    public Guid? CreatedByOperationId { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<Segment> Segments { get; } = [];
    public List<SegmentScreenshot> Screenshots { get; } = [];
    public List<SegmentNote> Notes { get; } = [];
    public List<PendingActionItem> PendingInboxItems { get; } = [];
}

public sealed class Segment : AuditedEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VideoId { get; init; }
    public Guid SegmentGenerationId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public decimal StartSeconds { get; init; }
    public decimal? EndSeconds { get; init; }
    public string TitleOriginal { get; init; } = string.Empty;
    public string? TitleOverride { get; set; }
    public string? SummaryOriginal { get; init; }
    public string? SummaryOverride { get; set; }
    public string? LlmModel { get; init; }
    public string? LlmPromptVersion { get; init; }
    public bool IsActive { get; set; } = true;
    public bool RequiresEmbeddingApproval { get; set; }
    public List<SegmentTranscriptRange> TranscriptRanges { get; } = [];
}

public sealed class SegmentTranscriptRange
{
    public Guid SegmentId { get; init; }
    public Guid TranscriptCueId { get; init; }
}

public sealed class SegmentScreenshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VideoId { get; init; }
    public Guid SegmentGenerationId { get; init; }
    public bool IsActive { get; set; } = true;
}

public sealed class SegmentNote
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SegmentId { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class PendingActionItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
}
