using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamingDigest.Application;

public sealed record RepositoryMetadata(
    string CanonicalUrl,
    string Host,
    string? Owner,
    string? RepositoryName,
    string? Description,
    int? Stars,
    string? Language,
    string? DefaultBranch,
    bool IsPublic,
    string? LicenseName,
    string? ReadmeContent = null,
    string? LicenseContent = null,
    string? DeepWikiUrl = null);

public sealed record RepositoryMetadataResult(
    RepositoryMetadata? Metadata,
    bool IsSuccess,
    bool IsRateLimited,
    int? StatusCode,
    string? ErrorMessage);

public interface IRepositoryMetadataService
{
    RepositoryMetadataResult Fetch(string? url);
}

public interface IRepositoryMetadataAdapter
{
    bool CanHandle(RepositoryHostKind hostKind);
    RepositoryMetadataResult Fetch(RepositoryHostDetectionResult detection);
}

public sealed class RepositoryMetadataService : IRepositoryMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRepositoryHostDetectionService _hostDetectionService;
    private readonly IReadOnlyList<IRepositoryMetadataAdapter> _adapters;

    public RepositoryMetadataService()
        : this(new RepositoryHostDetectionService(), new HttpClient(), null)
    {
    }

    public RepositoryMetadataService(IRepositoryHostDetectionService hostDetectionService, HttpClient httpClient)
        : this(hostDetectionService, httpClient, null)
    {
    }

    public RepositoryMetadataService(
        IRepositoryHostDetectionService hostDetectionService,
        HttpClient httpClient,
        IEnumerable<IRepositoryMetadataAdapter>? adapters)
    {
        _hostDetectionService = hostDetectionService ?? throw new ArgumentNullException(nameof(hostDetectionService));
        _adapters = adapters?.ToList() ?? new List<IRepositoryMetadataAdapter>
        {
            new GitHubRepositoryMetadataAdapter(httpClient ?? throw new ArgumentNullException(nameof(httpClient)))
        };
    }

    public RepositoryMetadataResult Fetch(string? url)
    {
        var detection = _hostDetectionService.Detect(url);
        if (detection is null || !detection.IsRepositoryUrl)
        {
            return new RepositoryMetadataResult(null, false, false, null, "Unsupported repository URL.");
        }

        foreach (var adapter in _adapters.Where(adapter => adapter.CanHandle(detection.Kind)))
        {
            return adapter.Fetch(detection);
        }

        return new RepositoryMetadataResult(null, false, false, null, $"No metadata adapter available for {detection.Kind}.");
    }
}

public sealed class GitHubRepositoryMetadataAdapter : IRepositoryMetadataAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public GitHubRepositoryMetadataAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public bool CanHandle(RepositoryHostKind hostKind) => hostKind == RepositoryHostKind.GitHub;

    public RepositoryMetadataResult Fetch(RepositoryHostDetectionResult detection)
    {
        if (detection.Owner is null || detection.RepositoryName is null)
        {
            return new RepositoryMetadataResult(null, false, false, null, "Repository owner/name could not be determined.");
        }

        var requestUri = $"https://api.github.com/repos/{Uri.EscapeDataString(detection.Owner)}/{Uri.EscapeDataString(detection.RepositoryName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("streaming-digest", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = _httpClient.Send(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new RepositoryMetadataResult(null, false, true, (int)response.StatusCode, "GitHub API rate limit exceeded.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new RepositoryMetadataResult(null, false, false, (int)response.StatusCode, "Repository not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new RepositoryMetadataResult(null, false, false, (int)response.StatusCode, "GitHub metadata request failed.");
            }

            var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var repo = JsonSerializer.Deserialize<GitHubRepositoryPayload>(payload, JsonOptions);
            if (repo is null)
            {
                return new RepositoryMetadataResult(null, false, false, (int)response.StatusCode, "GitHub metadata payload was empty.");
            }

            var readmeContent = TryFetchRepositoryDocumentContent(detection, "readme");
            var licenseContent = TryFetchRepositoryDocumentContent(detection, "license");

            return new RepositoryMetadataResult(
                new RepositoryMetadata(
                    detection.CanonicalUrl,
                    detection.Host,
                    detection.Owner,
                    detection.RepositoryName,
                    repo.Description,
                    repo.StargazersCount,
                    repo.Language,
                    repo.DefaultBranch,
                    !repo.IsPrivate,
                    repo.License?.SpdxId,
                    readmeContent,
                    licenseContent,
                    TryFetchDeepWikiUrl(detection)),
                true,
                false,
                (int)response.StatusCode,
                null);
        }
        catch (Exception ex)
        {
            return new RepositoryMetadataResult(null, false, false, null, ex.Message);
        }
    }

    private string? TryFetchRepositoryDocumentContent(RepositoryHostDetectionResult detection, string documentKind)
    {
        var requestUri = $"https://api.github.com/repos/{Uri.EscapeDataString(detection.Owner!)}/{Uri.EscapeDataString(detection.RepositoryName!)}/{documentKind}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("streaming-digest", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = _httpClient.Send(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var document = JsonSerializer.Deserialize<GitHubDocumentPayload>(payload, JsonOptions);
            if (document is null || string.IsNullOrWhiteSpace(document.Content))
            {
                return null;
            }

            if (!string.Equals(document.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(document.Content));
        }
        catch
        {
            return null;
        }
    }

    private string? TryFetchDeepWikiUrl(RepositoryHostDetectionResult detection)
    {
        if (detection.Owner is null || detection.RepositoryName is null)
        {
            return null;
        }

        var deepWikiUrl = $"https://deepwiki.com/{Uri.EscapeDataString(detection.Owner)}/{Uri.EscapeDataString(detection.RepositoryName)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, deepWikiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("streaming-digest", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        try
        {
            using var response = _httpClient.Send(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return payload.Contains("index your code", StringComparison.OrdinalIgnoreCase)
                ? null
                : deepWikiUrl;
        }
        catch
        {
            return null;
        }
    }

    private sealed class GitHubRepositoryPayload
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("stargazers_count")]
        public int? StargazersCount { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("default_branch")]
        public string? DefaultBranch { get; set; }

        [JsonPropertyName("private")]
        public bool IsPrivate { get; set; }

        [JsonPropertyName("license")]
        public GitHubLicensePayload? License { get; set; }
    }

    private sealed class GitHubDocumentPayload
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }
    }

    private sealed class GitHubLicensePayload
    {
        [JsonPropertyName("spdx_id")]
        public string? SpdxId { get; set; }
    }
}
