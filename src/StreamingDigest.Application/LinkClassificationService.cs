namespace StreamingDigest.Application;

public enum LinkClassification
{
    Unknown,
    CodeRepository,
    WebsiteResource,
    AdSponsor,
    Affiliate,
    Social,
    Newsletter,
    Course,
    Merch,
    Other
}

public sealed record LinkClassificationResult(LinkClassification Classification, string Method, double Confidence);

public interface ILinkClassificationService
{
    LinkClassificationResult Classify(string? url);
}

public sealed class LinkClassificationService : ILinkClassificationService
{
    public LinkClassificationResult Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new LinkClassificationResult(LinkClassification.Unknown, "rule", 0.0);
        }

        if (!TryNormalizeUrl(url, out var normalized))
        {
            return new LinkClassificationResult(LinkClassification.Other, "rule", 0.0);
        }

        var uri = normalized!;
        var host = uri.Host;
        var path = uri.AbsolutePath;
        var combined = $"{host} {path} {uri.Query}";

        if (LooksLikeCodeRepository(host, path))
        {
            return new LinkClassificationResult(LinkClassification.CodeRepository, "rule", 0.95);
        }

        if (LooksLikeAdSponsor(host, combined))
        {
            return new LinkClassificationResult(LinkClassification.AdSponsor, "rule", 0.9);
        }

        if (LooksLikeAffiliate(host, combined))
        {
            return new LinkClassificationResult(LinkClassification.Affiliate, "rule", 0.9);
        }

        if (LooksLikeNewsletter(host, combined))
        {
            return new LinkClassificationResult(LinkClassification.Newsletter, "rule", 0.9);
        }

        if (LooksLikeCourse(host, combined))
        {
            return new LinkClassificationResult(LinkClassification.Course, "rule", 0.9);
        }

        if (LooksLikeMerch(host, combined))
        {
            return new LinkClassificationResult(LinkClassification.Merch, "rule", 0.9);
        }

        if (LooksLikeSocial(host, combined))
        {
            return new LinkClassificationResult(LinkClassification.Social, "rule", 0.9);
        }

        return new LinkClassificationResult(LinkClassification.WebsiteResource, "rule", 0.8);
    }

    private static bool TryNormalizeUrl(string rawUrl, out Uri? uri)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out uri))
        {
            return uri is not null && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        var withScheme = rawUrl.Trim();
        if (!withScheme.StartsWith("//", StringComparison.Ordinal) && !withScheme.Contains(':', StringComparison.Ordinal))
        {
            withScheme = $"https://{withScheme}";
        }

        return Uri.TryCreate(withScheme, UriKind.Absolute, out uri)
            && uri is not null && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool LooksLikeCodeRepository(string host, string path)
    {
        var hostLower = host.ToLowerInvariant();
        var pathLower = path.ToLowerInvariant();

        return hostLower is "github.com" or "www.github.com"
            || hostLower is "gitlab.com" or "www.gitlab.com"
            || hostLower is "bitbucket.org" or "www.bitbucket.org"
            || hostLower is "raw.githubusercontent.com" or "www.raw.githubusercontent.com"
            || hostLower.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || hostLower.EndsWith("gitlab.com", StringComparison.OrdinalIgnoreCase)
            || hostLower.EndsWith("bitbucket.org", StringComparison.OrdinalIgnoreCase)
            || pathLower.Contains("/blob/", StringComparison.OrdinalIgnoreCase)
            || pathLower.Contains("/tree/", StringComparison.OrdinalIgnoreCase)
            || pathLower.Contains("/releases/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAdSponsor(string host, string combined)
    {
        return ContainsAny(combined, "sponsor", "sponsored", "support", "patreon", "buymeacoffee", "ko-fi", "ads", "advertisement", "promoted");
    }

    private static bool LooksLikeAffiliate(string host, string combined)
    {
        return ContainsAny(host, "amzn.to", "amazon", "ebay", "affiliate", "referral", "commission", "partners")
            || ContainsAny(combined, "affiliate", "referral", "commission", "partner", "partnered");
    }

    private static bool LooksLikeNewsletter(string host, string combined)
    {
        return ContainsAny(host, "substack", "mailchi", "buttondown", "tinyletter", "convertkit", "newsletters")
            || ContainsAny(combined, "newsletter", "subscribe", "mailing-list");
    }

    private static bool LooksLikeCourse(string host, string combined)
    {
        return ContainsAny(host, "udemy", "coursera", "pluralsight", "frontendmasters", "skillshare", "academy")
            || ContainsAny(combined, "/course/", "/courses/", "course", "bootcamp", "learning-path");
    }

    private static bool LooksLikeMerch(string host, string combined)
    {
        return ContainsAny(host, "etsy", "shopify", "redbubble", "printful", "spreadshirt", "teepublic", "merch")
            || ContainsAny(combined, "/shop/", "/store/", "merch", "merchandise");
    }

    private static bool LooksLikeSocial(string host, string combined)
    {
        return ContainsAny(host, "youtube.com", "youtu.be", "x.com", "twitter.com", "instagram.com", "facebook.com", "reddit.com", "linkedin.com", "mastodon", "tiktok.com", "discord", "twitch.tv")
            || ContainsAny(combined, "/@", "/u/", "/posts/", "social")
            || host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Contains("youtube", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string candidate, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (candidate.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
