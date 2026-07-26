using System.Text.RegularExpressions;

namespace StreamingDigest.Application;

public enum VideoLinkSource
{
    Description,
    PinnedComment
}

public sealed record ExtractedVideoLink(string Url, VideoLinkSource Source);

public interface IVideoLinkExtractionService
{
    IReadOnlyList<ExtractedVideoLink> Extract(string? descriptionText, string? pinnedCommentText);
}

public sealed class VideoLinkExtractionService : IVideoLinkExtractionService
{
    private static readonly Regex LinkPattern = new(
        @"\[[^\]]+\]\((?<markdownUrl><[^>]+>|[^)\s]+)\)|(?<bareUrl><[^>\s]+>|https?://[^\s<>()]+|www\.[^\s<>()]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<ExtractedVideoLink> Extract(string? descriptionText, string? pinnedCommentText)
    {
        var results = new List<ExtractedVideoLink>();
        AddLinks(results, descriptionText, VideoLinkSource.Description);
        AddLinks(results, pinnedCommentText, VideoLinkSource.PinnedComment);
        return results;
    }

    private static void AddLinks(ICollection<ExtractedVideoLink> results, string? text, VideoLinkSource source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in EnumerateMatches(text))
        {
            var normalized = NormalizeUrl(match);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var key = $"{source}:{normalized}";
            if (!seen.Add(key))
            {
                continue;
            }

            results.Add(new ExtractedVideoLink(normalized, source));
        }
    }

    private static IEnumerable<string> EnumerateMatches(string text)
    {
        foreach (Match match in LinkPattern.Matches(text))
        {
            if (match.Groups["markdownUrl"].Success)
            {
                yield return match.Groups["markdownUrl"].Value;
            }
            else if (match.Groups["bareUrl"].Success)
            {
                yield return match.Groups["bareUrl"].Value;
            }
        }
    }

    private static string NormalizeUrl(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
        {
            trimmed = trimmed[1..^1];
        }

        trimmed = trimmed.Trim();
        while (trimmed.Length > 0 && IsTrailingPunctuation(trimmed[^1]))
        {
            trimmed = trimmed[..^1];
        }

        trimmed = trimmed.Trim();
        while (trimmed.Length > 0 && trimmed[0] is '(' or '[' or '{' || trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Trim();
    }

    private static bool IsTrailingPunctuation(char value) => value is '.' or ',' or ';' or ':' or '!' or '?';
}
