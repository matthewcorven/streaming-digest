using System.Text;

namespace StreamingDigest.Domain;

internal static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source",
        "utm_medium",
        "utm_campaign",
        "utm_term",
        "utm_content",
        "utm_id",
        "utm_reader",
        "utm_name",
        "utm_cid",
        "utm_referrer",
        "utm_social",
        "utm_social-type",
        "fbclid",
        "gclid",
        "gclsrc",
        "dclid",
        "mc_cid",
        "mc_eid"
    };

    public static string NormalizeRequiredUrl(string? value)
        => NormalizeGenericUrlCore(value) ?? string.Empty;

    public static string? NormalizeOptionalUrl(string? value)
        => NormalizeGenericUrlCore(value);

    public static string NormalizeChannelSourceUrl(string? youtubeChannelId, string? value)
    {
        var normalizedChannelId = youtubeChannelId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedChannelId))
        {
            return $"https://www.youtube.com/channel/{normalizedChannelId}";
        }

        return NormalizeRequiredUrl(value);
    }

    public static string NormalizePlatformVideoUrl(string? platform, string? platformVideoId, string? youtubeVideoId, string? value)
    {
        var canonicalUrl = TryBuildCanonicalYoutubePlatformUrl(platform, platformVideoId, youtubeVideoId, value);
        return canonicalUrl ?? NormalizeRequiredUrl(value);
    }

    public static string NormalizeVideoUrl(string? platform, string? platformVideoId, string? youtubeVideoId, string? value)
    {
        var canonicalUrl = TryBuildCanonicalYoutubeWatchUrl(platform, platformVideoId, youtubeVideoId, value);
        return canonicalUrl ?? NormalizeRequiredUrl(value);
    }

    private static string? TryBuildCanonicalYoutubePlatformUrl(string? platform, string? platformVideoId, string? youtubeVideoId, string? value)
    {
        var videoId = ResolveYoutubeVideoId(platform, platformVideoId, youtubeVideoId, value);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        return "https://www.youtube.com/watch";
    }

    private static string? TryBuildCanonicalYoutubeWatchUrl(string? platform, string? platformVideoId, string? youtubeVideoId, string? value)
    {
        var videoId = ResolveYoutubeVideoId(platform, platformVideoId, youtubeVideoId, value);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        return $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
    }

    private static string? ResolveYoutubeVideoId(string? platform, string? platformVideoId, string? youtubeVideoId, string? value)
    {
        if (!IsYoutubePlatform(platform) && !IsYoutubeUrl(value))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(platformVideoId))
        {
            return platformVideoId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(youtubeVideoId))
        {
            return youtubeVideoId.Trim();
        }

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == "youtu.be")
        {
            return uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        var queryVideoId = TryGetQueryParameter(uri.Query, "v");
        if (!string.IsNullOrWhiteSpace(queryVideoId))
        {
            return queryVideoId;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] is "shorts" or "embed" or "live")
        {
            return segments[1];
        }

        return null;
    }

    private static string? NormalizeGenericUrlCore(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.IsNullOrWhiteSpace(value) ? null : string.Empty;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        if (!IsHttpScheme(uri.Scheme))
        {
            return trimmed;
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme.ToLowerInvariant());
        builder.Append("://");
        builder.Append(GetCanonicalHost(uri));

        if (!uri.IsDefaultPort)
        {
            builder.Append(':');
            builder.Append(uri.Port);
        }

        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (string.IsNullOrEmpty(path))
        {
            builder.Append('/');
        }
        else
        {
            builder.Append('/');
            builder.Append(path.TrimEnd('/'));
        }

        var query = RemoveTrackingParameters(uri.Query);
        if (!string.IsNullOrEmpty(query))
        {
            builder.Append('?');
            builder.Append(query);
        }

        return builder.ToString();
    }

    private static string GetCanonicalHost(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        return host switch
        {
            "youtube.com" or "m.youtube.com" or "youtu.be" => "www.youtube.com",
            _ => host
        };
    }

    private static bool IsYoutubePlatform(string? platform)
        => string.Equals(platform?.Trim(), "youtube", StringComparison.OrdinalIgnoreCase);

    private static bool IsYoutubeUrl(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
           && (uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase));

    private static bool IsHttpScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string RemoveTrackingParameters(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var keptSegments = new List<string>();
        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            var rawName = equalsIndex >= 0 ? segment[..equalsIndex] : segment;
            var name = Uri.UnescapeDataString(rawName);
            if (string.IsNullOrWhiteSpace(name) || TrackingParameterNames.Contains(name))
            {
                continue;
            }

            keptSegments.Add(segment);
        }

        return string.Join('&', keptSegments);
    }

    private static string? TryGetQueryParameter(string query, string name)
    {
        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            var rawName = equalsIndex >= 0 ? segment[..equalsIndex] : segment;
            if (!string.Equals(Uri.UnescapeDataString(rawName), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rawValue = equalsIndex >= 0 ? segment[(equalsIndex + 1)..] : string.Empty;
            return Uri.UnescapeDataString(rawValue);
        }

        return null;
    }
}
