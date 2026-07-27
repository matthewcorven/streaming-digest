namespace StreamingDigest.Domain;

public sealed class ScrapedPage : AuditedEntity
{
    public Guid Id { get; set; }
    public Guid ExternalResourceId { get; set; }
    public string FinalUrl { get; set; } = string.Empty;
    public string? TitleOriginal { get; set; }
    public string? TitleOverride { get; set; }
    public string? DescriptionOriginal { get; set; }
    public string? DescriptionOverride { get; set; }
    public string? OpenGraphJson { get; set; }
    public string? VisibleTextOriginal { get; set; }
    public string? VisibleTextOverride { get; set; }
    public bool? RobotsAllowed { get; set; }
    public string ScrapeStatus { get; set; } = string.Empty;
    public string? ExclusionReason { get; set; }
    public int? HttpStatus { get; set; }
    public string? ContentType { get; set; }
    public string? ContentHash { get; set; }
    public int? FetchDurationMs { get; set; }
    public long? PageSizeBytes { get; set; }
    public DateTimeOffset? ScrapedAt { get; set; }
    public string? RawHtmlDebugPath { get; set; }
    public string? ErrorSummary { get; set; }
}
