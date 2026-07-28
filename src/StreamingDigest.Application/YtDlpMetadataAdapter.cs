using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

public sealed class YtDlpMetadataAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public Channel AdaptChannelMetadata(string? payload, DateTimeOffset? fetchedAt = null)
    {
        var metadata = ParseChannelMetadata(payload);
        var channel = new Channel
        {
            YoutubeChannelId = metadata.Id ?? string.Empty,
            NameOriginal = string.IsNullOrWhiteSpace(metadata.Title) ? "Untitled channel" : metadata.Title.Trim(),
            DescriptionOriginal = metadata.Description,
            ProfileUrl = BuildChannelProfileUrl(metadata.Id),
            SourceUrl = BuildChannelSourceUrl(metadata.Id)
        };

        if (fetchedAt is not null)
        {
            channel.LastProbeAt = fetchedAt;
        }

        return channel;
    }

    public Video AdaptVideoMetadata(string? payload, Guid? channelId = null, DateTimeOffset? fetchedAt = null, int? minDurationSeconds = null)
    {
        var metadata = ParseVideoMetadata(payload);
        var video = new Video(Guid.NewGuid(), string.IsNullOrWhiteSpace(metadata.Title) ? "Untitled video" : metadata.Title.Trim())
        {
            ChannelId = channelId ?? Guid.Empty,
            AuthorOriginal = string.IsNullOrWhiteSpace(metadata.Uploader) ? string.Empty : metadata.Uploader.Trim(),
            DescriptionOriginal = metadata.Description,
            PublishedAt = metadata.PublishedAt,
            DurationSeconds = metadata.DurationSeconds,
            YoutubeVideoId = metadata.Id ?? string.Empty,
            VideoUrl = BuildVideoUrl(metadata.Id),
            ChaptersJson = metadata.Chapters is { Count: > 0 } ? JsonSerializer.Serialize(metadata.Chapters, JsonOptions) : null,
            CaptionsJson = metadata.Captions is { Count: > 0 } ? JsonSerializer.Serialize(metadata.Captions, JsonOptions) : null,
            RawMetadataJson = payload,
            MetadataFetchedAt = fetchedAt,
            IsLongForm = minDurationSeconds.HasValue
                ? VideoIngestionFilter.ClassifyIsLongForm(metadata.DurationSeconds, minDurationSeconds.Value)
                : true
        };

        return video;
    }

    public YtDlpChannelMetadata ParseChannelMetadata(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new YtDlpChannelMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<YtDlpChannelMetadata>(payload, JsonOptions) ?? new YtDlpChannelMetadata();
        }
        catch (JsonException)
        {
            return new YtDlpChannelMetadata();
        }
    }

    public YtDlpVideoMetadata ParseVideoMetadata(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new YtDlpVideoMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<YtDlpVideoMetadata>(payload, JsonOptions) ?? new YtDlpVideoMetadata();
        }
        catch (JsonException)
        {
            return new YtDlpVideoMetadata();
        }
    }

    private static string BuildChannelSourceUrl(string? youtubeChannelId)
        => string.IsNullOrWhiteSpace(youtubeChannelId)
            ? string.Empty
            : $"https://www.youtube.com/channel/{Uri.EscapeDataString(youtubeChannelId)}";

    private static string BuildChannelProfileUrl(string? youtubeChannelId)
        => string.IsNullOrWhiteSpace(youtubeChannelId)
            ? string.Empty
            : $"https://www.youtube.com/channel/{Uri.EscapeDataString(youtubeChannelId)}";

    private static string BuildVideoUrl(string? youtubeVideoId)
        => string.IsNullOrWhiteSpace(youtubeVideoId)
            ? string.Empty
            : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(youtubeVideoId)}";

    public static DateTimeOffset? ParsePublishedAt(string? uploadDate, long? releaseTimestamp, long? timestamp)
    {
        if (!string.IsNullOrWhiteSpace(uploadDate)
            && DateTime.TryParseExact(uploadDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedUploadDate))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(parsedUploadDate, DateTimeKind.Utc), TimeSpan.Zero);
        }

        if (releaseTimestamp is long releaseTimestampValue)
        {
            return DateTimeOffset.FromUnixTimeSeconds(releaseTimestampValue);
        }

        if (timestamp is long timestampValue)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestampValue);
        }

        return null;
    }
}

public sealed class YtDlpChannelMetadata
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("uploader")]
    public string? Uploader { get; set; }
}

public sealed class YtDlpVideoMetadata
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("uploader")]
    public string? Uploader { get; set; }

    [JsonPropertyName("upload_date")]
    public string? UploadDate { get; set; }

    [JsonPropertyName("release_timestamp")]
    public long? ReleaseTimestamp { get; set; }

    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    [JsonPropertyName("duration")]
    public int? DurationSeconds { get; set; }

    [JsonPropertyName("chapters")]
    public List<YtDlpChapterMetadata>? Chapters { get; set; }

    [JsonPropertyName("captions")]
    public Dictionary<string, YtDlpCaptionMetadata>? Captions { get; set; }

    public DateTimeOffset? PublishedAt => YtDlpMetadataAdapter.ParsePublishedAt(UploadDate, ReleaseTimestamp, Timestamp);
}

public sealed class YtDlpChapterMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("start_time")]
    public double? StartTimeSeconds { get; set; }

    [JsonPropertyName("end_time")]
    public double? EndTimeSeconds { get; set; }
}

public sealed class YtDlpCaptionMetadata
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}
