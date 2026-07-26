namespace StreamingDigest.Domain;

public sealed class Channel : AuditedEntity
{
    private string _youtubeChannelId = string.Empty;
    private string _profileUrl = string.Empty;
    private string _sourceUrl = string.Empty;

    public Guid Id { get; set; }
    public string YoutubeChannelId
    {
        get => _youtubeChannelId;
        set
        {
            _youtubeChannelId = value?.Trim() ?? string.Empty;
            _sourceUrl = UrlCanonicalizer.NormalizeChannelSourceUrl(_youtubeChannelId, _sourceUrl);
        }
    }

    public string NameOriginal { get; set; } = string.Empty;
    public string? NameOverride { get; set; }
    public string ProfileUrl
    {
        get => _profileUrl;
        set => _profileUrl = UrlCanonicalizer.NormalizeRequiredUrl(value);
    }

    public string SourceUrl
    {
        get => _sourceUrl;
        set => _sourceUrl = UrlCanonicalizer.NormalizeChannelSourceUrl(_youtubeChannelId, value);
    }

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
