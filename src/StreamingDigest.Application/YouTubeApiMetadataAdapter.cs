using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

public sealed record YouTubeApiChannelResult(
    Channel? Channel,
    bool IsSuccess,
    bool IsApiKeyMissing,
    bool IsRateLimited,
    int? StatusCode,
    string? ErrorMessage);

public sealed record YouTubeApiVideoResult(
    Video? Video,
    bool IsSuccess,
    bool IsApiKeyMissing,
    bool IsRateLimited,
    int? StatusCode,
    string? ErrorMessage);

public sealed record YouTubeApiVideoListResult(
    IReadOnlyList<string> VideoIds,
    bool IsSuccess,
    bool IsApiKeyMissing,
    bool IsRateLimited,
    int? StatusCode,
    string? ErrorMessage);

public sealed class YouTubeApiMetadataAdapter
{
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public YouTubeApiMetadataAdapter(HttpClient httpClient, string? apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<YouTubeApiChannelResult> FetchChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new YouTubeApiChannelResult(null, false, true, false, null, "YouTube API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return new YouTubeApiChannelResult(null, false, false, false, null, "Channel ID is required.");
        }

        var url = $"{BaseUrl}/channels?part=snippet&id={Uri.EscapeDataString(channelId)}&key={Uri.EscapeDataString(_apiKey!)}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new YouTubeApiChannelResult(null, false, false, true, (int)response.StatusCode, "YouTube API rate limit exceeded.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new YouTubeApiChannelResult(null, false, false, false, (int)response.StatusCode, $"YouTube API returned {(int)response.StatusCode}.");
            }

            var parsed = JsonSerializer.Deserialize<YouTubeApiChannelListResponse>(body, JsonOptions);
            var item = parsed?.Items?.FirstOrDefault();

            if (item is null)
            {
                return new YouTubeApiChannelResult(null, false, false, false, (int)response.StatusCode, $"Channel '{channelId}' not found in YouTube API response.");
            }

            var channel = AdaptChannel(item);
            return new YouTubeApiChannelResult(channel, true, false, false, (int)response.StatusCode, null);
        }
        catch (HttpRequestException ex)
        {
            return new YouTubeApiChannelResult(null, false, false, false, null, ex.Message);
        }
        catch (JsonException ex)
        {
            return new YouTubeApiChannelResult(null, false, false, false, null, $"Failed to parse YouTube API channel response: {ex.Message}");
        }
    }

    public async Task<YouTubeApiVideoResult> FetchVideoAsync(string videoId, Guid? channelId = null, int? minDurationSeconds = null, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new YouTubeApiVideoResult(null, false, true, false, null, "YouTube API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(videoId))
        {
            return new YouTubeApiVideoResult(null, false, false, false, null, "Video ID is required.");
        }

        var url = $"{BaseUrl}/videos?part=snippet,contentDetails&id={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(_apiKey!)}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new YouTubeApiVideoResult(null, false, false, true, (int)response.StatusCode, "YouTube API rate limit exceeded.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new YouTubeApiVideoResult(null, false, false, false, (int)response.StatusCode, $"YouTube API returned {(int)response.StatusCode}.");
            }

            var parsed = JsonSerializer.Deserialize<YouTubeApiVideoListResponse>(body, JsonOptions);
            var item = parsed?.Items?.FirstOrDefault();

            if (item is null)
            {
                return new YouTubeApiVideoResult(null, false, false, false, (int)response.StatusCode, $"Video '{videoId}' not found in YouTube API response.");
            }

            var video = AdaptVideo(item, channelId, minDurationSeconds);
            return new YouTubeApiVideoResult(video, true, false, false, (int)response.StatusCode, null);
        }
        catch (HttpRequestException ex)
        {
            return new YouTubeApiVideoResult(null, false, false, false, null, ex.Message);
        }
        catch (JsonException ex)
        {
            return new YouTubeApiVideoResult(null, false, false, false, null, $"Failed to parse YouTube API video response: {ex.Message}");
        }
    }

    public async Task<YouTubeApiVideoListResult> ListChannelVideoIdsAsync(
        string channelId,
        DateTimeOffset? publishedAfter = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new YouTubeApiVideoListResult([], false, true, false, null, "YouTube API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return new YouTubeApiVideoListResult([], false, false, false, null, "Channel ID is required.");
        }

        var clampedMaxResults = Math.Clamp(maxResults, 1, 50);
        var url = $"{BaseUrl}/search?part=id&channelId={Uri.EscapeDataString(channelId)}&type=video&order=date&maxResults={clampedMaxResults}&key={Uri.EscapeDataString(_apiKey!)}";

        if (publishedAfter is DateTimeOffset after)
        {
            url += $"&publishedAfter={Uri.EscapeDataString(after.UtcDateTime.ToString("o"))}";
        }

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new YouTubeApiVideoListResult([], false, false, true, (int)response.StatusCode, "YouTube API rate limit exceeded.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new YouTubeApiVideoListResult([], false, false, false, (int)response.StatusCode, $"YouTube API returned {(int)response.StatusCode}.");
            }

            var parsed = JsonSerializer.Deserialize<YouTubeApiSearchListResponse>(body, JsonOptions);
            var videoIds = parsed?.Items?
                .Where(i => i.Id?.VideoId is not null)
                .Select(i => i.Id!.VideoId!)
                .ToList() ?? [];

            return new YouTubeApiVideoListResult(videoIds, true, false, false, (int)response.StatusCode, null);
        }
        catch (HttpRequestException ex)
        {
            return new YouTubeApiVideoListResult([], false, false, false, null, ex.Message);
        }
        catch (JsonException ex)
        {
            return new YouTubeApiVideoListResult([], false, false, false, null, $"Failed to parse YouTube API search response: {ex.Message}");
        }
    }

    private static Channel AdaptChannel(YouTubeApiChannelItem item)
    {
        var id = item.Id ?? string.Empty;
        return new Channel
        {
            YoutubeChannelId = id,
            NameOriginal = string.IsNullOrWhiteSpace(item.Snippet?.Title) ? "Untitled channel" : item.Snippet.Title.Trim(),
            DescriptionOriginal = item.Snippet?.Description,
            ProfileUrl = BuildChannelProfileUrl(id),
            SourceUrl = BuildChannelSourceUrl(id)
        };
    }

    private static Video AdaptVideo(YouTubeApiVideoItem item, Guid? channelId, int? minDurationSeconds)
    {
        var id = item.Id ?? string.Empty;
        var title = string.IsNullOrWhiteSpace(item.Snippet?.Title) ? "Untitled video" : item.Snippet.Title.Trim();
        var durationSeconds = ParseIso8601Duration(item.ContentDetails?.Duration);

        return new Video(Guid.NewGuid(), title)
        {
            ChannelId = channelId ?? Guid.Empty,
            YoutubeVideoId = id,
            VideoUrl = BuildVideoUrl(id),
            AuthorOriginal = item.Snippet?.ChannelTitle?.Trim() ?? string.Empty,
            DescriptionOriginal = item.Snippet?.Description,
            PublishedAt = item.Snippet?.PublishedAt,
            DurationSeconds = durationSeconds,
            IsLongForm = minDurationSeconds.HasValue
                ? VideoIngestionFilter.ClassifyIsLongForm(durationSeconds, minDurationSeconds.Value)
                : true
        };
    }

    public static int? ParseIso8601Duration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        // ISO 8601 duration: PT#H#M#S (e.g. PT1H30M45S, PT10M30S, PT45S)
        var span = duration.AsSpan();
        if (span.Length < 2 || span[0] != 'P')
        {
            return null;
        }

        int totalSeconds = 0;
        int pos = 1;

        if (pos < span.Length && span[pos] == 'T')
        {
            pos++;
        }

        int currentNumber = 0;
        bool inTime = true;

        while (pos < span.Length && inTime)
        {
            char c = span[pos];

            if (char.IsAsciiDigit(c))
            {
                currentNumber = currentNumber * 10 + (c - '0');
            }
            else
            {
                switch (c)
                {
                    case 'H':
                        totalSeconds += currentNumber * 3600;
                        currentNumber = 0;
                        break;
                    case 'M':
                        totalSeconds += currentNumber * 60;
                        currentNumber = 0;
                        break;
                    case 'S':
                        totalSeconds += currentNumber;
                        currentNumber = 0;
                        break;
                    default:
                        inTime = false;
                        break;
                }
            }

            pos++;
        }

        return totalSeconds > 0 ? totalSeconds : null;
    }

    private static string BuildChannelSourceUrl(string channelId)
        => string.IsNullOrWhiteSpace(channelId)
            ? string.Empty
            : $"https://www.youtube.com/channel/{Uri.EscapeDataString(channelId)}";

    private static string BuildChannelProfileUrl(string channelId)
        => string.IsNullOrWhiteSpace(channelId)
            ? string.Empty
            : $"https://www.youtube.com/channel/{Uri.EscapeDataString(channelId)}";

    private static string BuildVideoUrl(string videoId)
        => string.IsNullOrWhiteSpace(videoId)
            ? string.Empty
            : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
}

// YouTube Data API v3 response models

public sealed class YouTubeApiChannelListResponse
{
    [JsonPropertyName("items")]
    public List<YouTubeApiChannelItem>? Items { get; set; }
}

public sealed class YouTubeApiChannelItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("snippet")]
    public YouTubeApiChannelSnippet? Snippet { get; set; }
}

public sealed class YouTubeApiChannelSnippet
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("customUrl")]
    public string? CustomUrl { get; set; }
}

public sealed class YouTubeApiVideoListResponse
{
    [JsonPropertyName("items")]
    public List<YouTubeApiVideoItem>? Items { get; set; }
}

public sealed class YouTubeApiVideoItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("snippet")]
    public YouTubeApiVideoSnippet? Snippet { get; set; }

    [JsonPropertyName("contentDetails")]
    public YouTubeApiVideoContentDetails? ContentDetails { get; set; }
}

public sealed class YouTubeApiVideoSnippet
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("channelTitle")]
    public string? ChannelTitle { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class YouTubeApiVideoContentDetails
{
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
}

public sealed class YouTubeApiSearchListResponse
{
    [JsonPropertyName("items")]
    public List<YouTubeApiSearchItem>? Items { get; set; }

    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}

public sealed class YouTubeApiSearchItem
{
    [JsonPropertyName("id")]
    public YouTubeApiSearchItemId? Id { get; set; }
}

public sealed class YouTubeApiSearchItemId
{
    [JsonPropertyName("videoId")]
    public string? VideoId { get; set; }
}
