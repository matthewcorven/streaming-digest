namespace StreamingDigest.Domain;

public sealed class Video : AuditedEntity
{
    public Video(Guid id, string title)
    {
        Id = id;
        Title = title;
    }

    public Guid Id { get; init; }
    public string Title { get; set; }
    public string Platform { get; set; } = "youtube";
    public string PlatformVideoUrl { get; set; } = string.Empty;
    public string PlatformVideoId { get; set; } = string.Empty;
    public string YoutubeVideoId { get; set; } = string.Empty;
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public string AuthorOriginal { get; set; } = string.Empty;
    public string? AuthorOverride { get; set; }
    public string? DescriptionOriginal { get; set; }
    public string? DescriptionOverride { get; set; }
    public string VideoUrl { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsLongForm { get; set; } = true;
    public string IngestionStatus { get; set; } = "pending";
    public string TranscriptStatus { get; set; } = "unknown";
    public string ScreenshotStatus { get; set; } = "unknown";
    public string? ProcessingVersion { get; set; }
    public Guid? LastSuccessfulIngestionRunId { get; set; }
    public Guid? LastFailedIngestionRunId { get; set; }
    public DateTimeOffset? MetadataFetchedAt { get; set; }
    public DateTimeOffset? TranscriptFetchedAt { get; set; }
    public DateTimeOffset? LinksExtractedAt { get; set; }
    public DateTimeOffset? SearchIndexedAt { get; set; }
    public string? RawMetadataJson { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled video" : Title.Trim();
}