namespace StreamingDigest.Domain;

public sealed class IngestionRun
{
    public Guid Id { get; set; }
    public Guid? OperationId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ScheduleId { get; set; }
    public string RunType { get; set; } = string.Empty;
    public string TriggeredBy { get; set; } = string.Empty;
    public Guid? RequestedByUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ChannelsChecked { get; set; }
    public int NewVideosFound { get; set; }
    public int VideosIngested { get; set; }
    public int VideosFailed { get; set; }
    public int VideosSkipped { get; set; }
    public int TranscriptsFound { get; set; }
    public int TranscriptsMissing { get; set; }
    public int RepositoriesFound { get; set; }
    public string? ConfigSnapshotJson { get; set; }
    public string? SummaryJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
