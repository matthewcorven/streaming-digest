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
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<Segment> Segments { get; } = [];
    public List<SegmentScreenshot> Screenshots { get; } = [];
    public List<SegmentNote> Notes { get; } = [];
    public List<PendingActionItem> PendingInboxItems { get; } = [];
}

public sealed class Segment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SegmentGenerationId { get; init; }
    public int Sequence { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool RequiresEmbeddingApproval { get; set; }
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
