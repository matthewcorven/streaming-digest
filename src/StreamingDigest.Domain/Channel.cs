namespace StreamingDigest.Domain;

public sealed class Channel : AuditedEntity
{
    public Guid Id { get; set; }
    public string YoutubeChannelId { get; set; } = string.Empty;
    public string NameOriginal { get; set; } = string.Empty;
    public string? NameOverride { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string? DescriptionOriginal { get; set; }
    public string? DescriptionOverride { get; set; }
    public bool IsPaused { get; set; }
    public int? DefaultMaxAgeDays { get; set; }
    public int? DefaultBackfillMaxVideos { get; set; }
    public bool IsDegraded { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastProbeAt { get; set; }
    public DateTimeOffset? DegradedAt { get; set; }
    public DateTimeOffset? LastIngestedAt { get; set; }
    public string? LastIngestionStatus { get; set; }
}
