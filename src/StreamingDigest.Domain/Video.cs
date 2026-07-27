namespace StreamingDigest.Domain;

public sealed class Video : AuditedEntity
{
    private string _platform = "youtube";
    private string _platformVideoUrl = string.Empty;
    private string _platformVideoId = string.Empty;
    private string _youtubeVideoId = string.Empty;
    private string _videoUrl = string.Empty;
    private string? _thumbnailUrl;

    public Video(Guid id, string title)
    {
        Id = id;
        Title = title;
    }

    public Guid Id { get; init; }
    public string Title { get; set; }
    public string Platform
    {
        get => _platform;
        set
        {
            _platform = string.IsNullOrWhiteSpace(value) ? "youtube" : value.Trim();
            RecanonicalizeUrls();
        }
    }

    public string PlatformVideoUrl
    {
        get => _platformVideoUrl;
        set => _platformVideoUrl = UrlCanonicalizer.NormalizePlatformVideoUrl(_platform, _platformVideoId, _youtubeVideoId, value);
    }

    public string PlatformVideoId
    {
        get => _platformVideoId;
        set
        {
            _platformVideoId = value?.Trim() ?? string.Empty;
            RecanonicalizeUrls();
        }
    }

    public string YoutubeVideoId
    {
        get => _youtubeVideoId;
        set
        {
            _youtubeVideoId = value?.Trim() ?? string.Empty;
            RecanonicalizeUrls();
        }
    }

    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public string AuthorOriginal { get; set; } = string.Empty;
    public string? AuthorOverride { get; set; }
    public string? DescriptionOriginal { get; set; }
    public string? DescriptionOverride { get; set; }
    public string VideoUrl
    {
        get => _videoUrl;
        set => _videoUrl = UrlCanonicalizer.NormalizeVideoUrl(_platform, _platformVideoId, _youtubeVideoId, value);
    }

    public DateTimeOffset? PublishedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ChaptersJson { get; set; }
    public string? CaptionsJson { get; set; }
    public string? ThumbnailUrl
    {
        get => _thumbnailUrl;
        set => _thumbnailUrl = UrlCanonicalizer.NormalizeOptionalUrl(value);
    }

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

    private void RecanonicalizeUrls()
    {
        _platformVideoUrl = UrlCanonicalizer.NormalizePlatformVideoUrl(_platform, _platformVideoId, _youtubeVideoId, _platformVideoUrl);
        _videoUrl = UrlCanonicalizer.NormalizeVideoUrl(_platform, _platformVideoId, _youtubeVideoId, _videoUrl);
        _thumbnailUrl = UrlCanonicalizer.NormalizeOptionalUrl(_thumbnailUrl);
    }
}