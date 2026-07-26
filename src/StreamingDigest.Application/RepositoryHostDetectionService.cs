using System.Collections.Generic;
using System.Linq;

namespace StreamingDigest.Application;

public enum RepositoryHostKind
{
    Unknown,
    GitHub,
    GitLab,
    Bitbucket,
    Other
}

public sealed record RepositoryHostDetectionResult(
    RepositoryHostKind Kind,
    string Host,
    string CanonicalUrl,
    string? Owner,
    string? RepositoryName,
    bool IsRepositoryUrl);

public interface IRepositoryHostDetectionService
{
    RepositoryHostDetectionResult? Detect(string? url);
}

public sealed class RepositoryHostDetectionService : IRepositoryHostDetectionService
{
    public RepositoryHostDetectionResult? Detect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmedUrl = url.Trim();
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            if (!trimmedUrl.StartsWith("//", StringComparison.Ordinal) && !trimmedUrl.Contains(':', StringComparison.Ordinal))
            {
                trimmedUrl = $"https://{trimmedUrl}";
                if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out uri))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        if (!IsHttpScheme(uri.Scheme))
        {
            return null;
        }

        return Detect(uri);
    }

    internal RepositoryHostDetectionResult? Detect(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        var kind = ResolveHostKind(host);
        if (kind == RepositoryHostKind.Unknown)
        {
            return null;
        }

        var canonicalHost = ResolveCanonicalHost(kind);
        var canonicalPath = BuildCanonicalPath(kind, host, uri.AbsolutePath);
        var canonicalUrl = BuildCanonicalUrl(uri.Scheme, canonicalHost, canonicalPath);

        return new RepositoryHostDetectionResult(
            kind,
            canonicalHost,
            canonicalUrl,
            TryExtractOwner(kind, canonicalPath),
            TryExtractRepositoryName(kind, canonicalPath),
            !string.IsNullOrWhiteSpace(canonicalPath));
    }

    private static RepositoryHostKind ResolveHostKind(string host)
    {
        return host switch
        {
            "github.com" or "www.github.com" or "raw.githubusercontent.com" or "www.raw.githubusercontent.com" => RepositoryHostKind.GitHub,
            "gitlab.com" or "www.gitlab.com" => RepositoryHostKind.GitLab,
            "bitbucket.org" or "www.bitbucket.org" => RepositoryHostKind.Bitbucket,
            _ => host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                ? RepositoryHostKind.GitHub
                : RepositoryHostKind.Unknown
        };
    }

    private static string ResolveCanonicalHost(RepositoryHostKind kind)
        => kind switch
        {
            RepositoryHostKind.GitHub => "github.com",
            RepositoryHostKind.GitLab => "gitlab.com",
            RepositoryHostKind.Bitbucket => "bitbucket.org",
            _ => ""
        };

    private static string BuildCanonicalUrl(string scheme, string canonicalHost, string? canonicalPath)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(scheme.ToLowerInvariant());
        builder.Append("://");
        builder.Append(canonicalHost);

        if (!string.IsNullOrWhiteSpace(canonicalPath))
        {
            builder.Append(canonicalPath);
        }
        else
        {
            builder.Append('/');
        }

        return builder.ToString();
    }

    private static string? BuildCanonicalPath(RepositoryHostKind kind, string host, string absolutePath)
    {
        var cleanedSegments = absolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (cleanedSegments.Length == 0)
        {
            return null;
        }

        return kind switch
        {
            RepositoryHostKind.GitHub => BuildGitHubCanonicalPath(cleanedSegments),
            RepositoryHostKind.GitLab => BuildGitLabCanonicalPath(cleanedSegments),
            RepositoryHostKind.Bitbucket => BuildBitbucketCanonicalPath(cleanedSegments),
            _ => null
        };
    }

    private static string? BuildGitHubCanonicalPath(IReadOnlyList<string> segments)
    {
        return segments.Count >= 2 ? $"/{segments[0]}/{segments[1]}" : null;
    }

    private static string? BuildGitLabCanonicalPath(IReadOnlyList<string> segments)
    {
        var normalizedSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (segment is "-" or "blob" or "tree" or "releases" or "issues" or "merge_requests" or "wiki" or "commits" or "pipelines" or "packages")
            {
                break;
            }

            normalizedSegments.Add(segment);
        }

        return normalizedSegments.Count >= 2 ? $"/{string.Join('/', normalizedSegments)}" : null;
    }

    private static string? BuildBitbucketCanonicalPath(IReadOnlyList<string> segments)
    {
        return segments.Count >= 2 ? $"/{segments[0]}/{segments[1]}" : null;
    }

    private static string? TryExtractOwner(RepositoryHostKind kind, string? canonicalPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalPath))
        {
            return null;
        }

        var segments = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        return kind == RepositoryHostKind.GitLab && segments.Length > 2
            ? string.Join('/', segments[..^1])
            : segments[0];
    }

    private static string? TryExtractRepositoryName(RepositoryHostKind kind, string? canonicalPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalPath))
        {
            return null;
        }

        var segments = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        return kind == RepositoryHostKind.GitLab && segments.Length > 2
            ? segments[^1]
            : segments[1];
    }

    private static bool IsHttpScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
