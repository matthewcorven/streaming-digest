using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;

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
public sealed record LinkClassificationExample(string Url, LinkClassification Classification, string? Reason = null);
public sealed record LinkClassificationCorrection(string Url, LinkClassification PreviousClassification, LinkClassification CorrectedClassification, string? Reason = null, bool IsActive = true);

public interface ILinkClassificationService
{
    LinkClassificationResult Classify(string? url);
    LinkClassificationResult Classify(string? url, IReadOnlyList<LinkClassificationExample>? examples);
    LinkClassificationResult Classify(string? url, IReadOnlyList<LinkClassificationCorrection>? corrections);
}

public sealed class LinkClassificationService : ILinkClassificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IRepositoryHostDetectionService RepositoryHostDetectionService = new RepositoryHostDetectionService();

    private readonly MeaiChatClientWrapper? _chatClientWrapper;
    private readonly ILogger<LinkClassificationService>? _logger;
    private readonly bool _llmEnabled;
    private readonly IModelReadinessGuard? _modelReadinessGuard;
    private readonly IModelReadinessNotifier? _modelReadinessNotifier;

    public LinkClassificationService()
        : this(null, null)
    {
    }

    public LinkClassificationService(
        MeaiChatClientWrapper? chatClientWrapper,
        ILogger<LinkClassificationService>? logger = null,
        IModelReadinessGuard? modelReadinessGuard = null,
        IModelReadinessNotifier? modelReadinessNotifier = null)
    {
        _chatClientWrapper = chatClientWrapper;
        _logger = logger;
        _llmEnabled = ResolveLlmEnabled();
        _modelReadinessGuard = modelReadinessGuard;
        _modelReadinessNotifier = modelReadinessNotifier;
    }

    public LinkClassificationResult Classify(string? url) => Classify(url, (IReadOnlyList<LinkClassificationExample>?)null);

    public LinkClassificationResult Classify(string? url, IReadOnlyList<LinkClassificationExample>? examples)
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
        var ruleResult = ApplyRuleBasedClassification(uri);
        if (!_llmEnabled)
        {
            return ruleResult;
        }

        var llmResult = TryClassifyWithLocalLlm(url, uri, examples);
        return llmResult ?? ruleResult;
    }

    public LinkClassificationResult Classify(string? url, IReadOnlyList<LinkClassificationCorrection>? corrections)
    {
        var examples = BuildPromptExamples(corrections);
        return Classify(url, examples);
    }

    private LinkClassificationResult? TryClassifyWithLocalLlm(string? url, Uri uri, IReadOnlyList<LinkClassificationExample>? examples)
    {
        if (_chatClientWrapper is null)
        {
            return null;
        }

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(url, uri, examples);
            var modelName = ResolveModelName();

            if (string.IsNullOrWhiteSpace(modelName))
            {
                return null;
            }

            var responseText = _chatClientWrapper.SendChatAsync(
                systemPrompt,
                userPrompt,
                modelName,
                temperature: 0.0,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            if (!TryParseLlmClassification(responseText, out var classification, out var confidence))
            {
                return null;
            }

            return new LinkClassificationResult(classification, confidence >= 0.8 ? "llm" : "rule+llm", confidence);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Local LLM classification failed for {Url}; falling back to rules", url);

            // WS-7 S5: surface a one-time notified degrade when the LLM is unreachable/unready
            // instead of only a silent heuristic/rule fallback.
            if (_modelReadinessGuard is not null && _modelReadinessNotifier is not null)
            {
                try
                {
                    var readiness = _modelReadinessGuard.CheckAsync(RuntimeRole.LLM, CancellationToken.None).GetAwaiter().GetResult();
                    _modelReadinessNotifier.ReportDegraded(
                        "S5",
                        readiness,
                        "LLM link classification skipped; rule-based classification used");
                }
                catch (Exception guardEx)
                {
                    _logger?.LogDebug(guardEx, "Model readiness guard check failed during S5 degrade handling.");
                }
            }

            return null;
        }
    }

    private static LinkClassificationResult ApplyRuleBasedClassification(Uri uri)
    {
        var host = uri.Host;
        var path = uri.AbsolutePath;
        var combined = $"{host} {path} {uri.Query}";

        if (LooksLikeCodeRepository(uri, host, path))
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

    private static bool LooksLikeCodeRepository(Uri uri, string host, string path)
    {
        var hostLower = host.ToLowerInvariant();
        var pathLower = path.ToLowerInvariant();

        if (RepositoryHostDetectionService.Detect(uri.ToString())?.IsRepositoryUrl == true)
        {
            return true;
        }

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

    private static IReadOnlyList<LinkClassificationExample> BuildPromptExamples(IEnumerable<LinkClassificationCorrection>? corrections)
    {
        if (corrections is null)
        {
            return Array.Empty<LinkClassificationExample>();
        }

        return corrections
            .Where(correction => correction is not null && correction.IsActive && !string.IsNullOrWhiteSpace(correction.Url))
            .Select(correction => new LinkClassificationExample(
                correction.Url,
                correction.CorrectedClassification,
                string.IsNullOrWhiteSpace(correction.Reason)
                    ? $"corrected from {correction.PreviousClassification}"
                    : correction.Reason))
            .Take(5)
            .ToArray();
    }

    private static string BuildSystemPrompt()
    {
        return "You classify URLs into the categories Unknown, CodeRepository, WebsiteResource, AdSponsor, Affiliate, Social, Newsletter, Course, Merch, or Other. Return compact JSON with classification and confidence fields only.";
    }

    private static string BuildUserPrompt(string? url, Uri uri, IReadOnlyList<LinkClassificationExample>? examples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Classify the following URL.");
        builder.AppendLine($"URL: {url}");
        builder.AppendLine($"Host: {uri.Host}");
        builder.AppendLine($"Path: {uri.AbsolutePath}");
        builder.AppendLine($"Query: {uri.Query}");

        if (examples is { Count: > 0 })
        {
            builder.AppendLine("Corrections/examples:");
            foreach (var example in examples.Take(5))
            {
                builder.AppendLine($"- {example.Url}: {example.Classification} ({example.Reason ?? "no reason"})");
            }
        }

        return builder.ToString();
    }

    private static bool TryParseLlmClassification(string content, out LinkClassification classification, out double confidence)
    {
        classification = LinkClassification.Unknown;
        confidence = 0.0;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalized = content.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            normalized = normalized.Trim('`', '\n', '\r', ' ');
        }

        if (!TryExtractJsonPayload(normalized, out var payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryReadNestedContent(root, out var nestedContent))
                {
                    if (!TryParseLlmClassification(nestedContent, out classification, out confidence))
                    {
                        return false;
                    }

                    return true;
                }

                if (root.TryGetProperty("classification", out var classificationProperty))
                {
                    var classificationValue = classificationProperty.GetString();
                    if (TryParseClassification(classificationValue, out classification))
                    {
                        confidence = ReadConfidence(root);
                        return true;
                    }
                }

                if (root.TryGetProperty("label", out var labelProperty))
                {
                    var classificationValue = labelProperty.GetString();
                    if (TryParseClassification(classificationValue, out classification))
                    {
                        confidence = ReadConfidence(root);
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryReadNestedContent(JsonElement root, out string content)
    {
        content = string.Empty;
        if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.Object)
        {
            if (messageElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
            {
                content = contentElement.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(content);
            }
        }

        return false;
    }

    private static double ReadConfidence(JsonElement root)
    {
        if (root.TryGetProperty("confidence", out var confidenceProperty))
        {
            if (confidenceProperty.ValueKind == JsonValueKind.Number && confidenceProperty.TryGetDouble(out var parsed))
            {
                return Math.Clamp(parsed, 0.0, 1.0);
            }
        }

        return 0.8;
    }

    private static bool TryParseClassification(string? value, out LinkClassification classification)
    {
        classification = LinkClassification.Unknown;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Enum.TryParse<LinkClassification>(value, true, out var parsed))
        {
            classification = parsed;
            return true;
        }

        return value.ToLowerInvariant() switch
        {
            "code repository" => (classification = LinkClassification.CodeRepository, true).Item2,
            "website resource" => (classification = LinkClassification.WebsiteResource, true).Item2,
            "ad sponsor" => (classification = LinkClassification.AdSponsor, true).Item2,
            "affiliate" => (classification = LinkClassification.Affiliate, true).Item2,
            "social" => (classification = LinkClassification.Social, true).Item2,
            "newsletter" => (classification = LinkClassification.Newsletter, true).Item2,
            "course" => (classification = LinkClassification.Course, true).Item2,
            "merch" => (classification = LinkClassification.Merch, true).Item2,
            "other" => (classification = LinkClassification.Other, true).Item2,
            _ => false
        };
    }

    private static bool TryExtractJsonPayload(string content, out string payload)
    {
        payload = string.Empty;
        var startIndex = content.IndexOf('{');
        if (startIndex < 0)
        {
            return false;
        }

        var depth = 0;
        for (var index = startIndex; index < content.Length; index++)
        {
            var current = content[index];
            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    payload = content[startIndex..(index + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    private bool ResolveLlmEnabled()
    {
        return _chatClientWrapper is not null && !string.IsNullOrWhiteSpace(ResolveModelName());
    }

    private static string? ResolveModelName()
    {
        return Environment.GetEnvironmentVariable("STREAMINGDIGEST_LLM_MODEL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? "llama3.2";
    }
}
