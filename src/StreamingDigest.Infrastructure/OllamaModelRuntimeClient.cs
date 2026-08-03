using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace StreamingDigest.Infrastructure;

/// <summary>
/// Ollama implementation of <see cref="Application.IModelRuntimeClient"/>. Sits beside
/// <see cref="Application.OllamaEmbeddingService"/> and resolves the same endpoint configuration
/// keys so the management seam and the inference seam share one host. This client is a thin
/// management surface (<c>/api/tags</c>, <c>/api/pull</c>, <c>/api/show</c>); it does not
/// perform inference and never routes through Semantic Kernel or Microsoft.Extensions.AI.
/// </summary>
public sealed class OllamaModelRuntimeClient : Application.IModelRuntimeClient
{
    internal const string ProviderName = "ollama";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public OllamaModelRuntimeClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    // Resolve the named client per operation so the pooled handler (and its DNS resolution)
    // rotates with SetHandlerLifetime instead of being pinned for the process lifetime.
    private HttpClient CreateClient() => _httpClientFactory.CreateClient("ollama-runtime");

    public async Task<IReadOnlyList<Application.ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = ResolveEndpoint();
        var response = await CreateClient().GetAsync(BuildUri(endpoint, "api/tags"), cancellationToken);
        var payload = await ReadJsonAsync<OllamaTagsResponse>(response, cancellationToken);

        var models = payload.Models ?? [];
        var result = new List<Application.ModelPresence>(models.Count);
        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                continue;
            }

            result.Add(new Application.ModelPresence(ProviderName, model.Name!, model.Digest, model.Size));
        }

        return result;
    }

    public async IAsyncEnumerable<Application.ModelPullProgress> PullModelAsync(
        string model,
        bool stream = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var endpoint = ResolveEndpoint();
        var requestUri = BuildUri(endpoint, "api/pull");
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new OllamaPullRequest(model, stream), options: JsonOptions)
        };

        using var response = await CreateClient().SendAsync(
            request,
            stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Ollama pull request for '{model}' failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        // Pull responses are newline-delimited JSON (NDJSON). When stream=false Ollama still
        // emits a single JSON object as the final line; reading it line-by-line handles both.
        using var streamContent = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(streamContent);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Application.ModelPullProgress? progress;
            try
            {
                var parsed = JsonSerializer.Deserialize<OllamaPullProgressResponse>(line, JsonOptions);
                if (parsed is null)
                {
                    continue;
                }

                // Ollama emits a terminal {"error":"..."} object when a pull fails mid-stream
                // (network error, model not found on library, etc.). Surface it rather than
                // silently ending the enumeration as if the pull succeeded.
                if (!string.IsNullOrWhiteSpace(parsed.Error))
                {
                    throw new InvalidOperationException($"Ollama pull for '{model}' failed: {parsed.Error}");
                }

                progress = MapPullProgress(parsed);
            }
            catch (JsonException)
            {
                // A non-JSON keepalive/status line should not abort the whole pull.
                continue;
            }

            if (progress is not null)
            {
                yield return progress;
            }
        }
    }

    public async Task<Application.ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var endpoint = ResolveEndpoint();
        var requestUri = BuildUri(endpoint, "api/show");
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new OllamaShowRequest(model), options: JsonOptions)
        };

        using var response = await CreateClient().SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<OllamaShowResponse>(response, cancellationToken);

        var details = payload.Details is null ? null : JsonSerializer.Serialize(payload.Details, JsonOptions);
        // Ollama nests `families` (and `family`) inside the `details` object, not at the
        // response root — see docs/api.md "Show Model Information".
        var families = payload.Details?.Families ?? [];
        return new Application.ModelRuntimeInfo(ProviderName, model, details, families);
    }

    private static Application.ModelPullProgress MapPullProgress(OllamaPullProgressResponse parsed)
    {
        long? total = parsed.Total;
        long? completed = parsed.Completed;
        int? percent = null;
        if (total is > 0 && completed is >= 0)
        {
            var ratio = (double)completed.Value / total.Value;
            ratio = Math.Clamp(ratio, 0d, 1d);
            // AwayFromZero rounding so 33.33% -> 33 and 50.5% -> 51 (UI-friendly, no banker's rounding drift).
            percent = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
        }

        return new Application.ModelPullProgress(parsed.Status ?? string.Empty, total, completed, percent);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Ollama runtime request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty runtime response.");
        return payload;
    }

    private string ResolveEndpoint()
        => ResolveConfigurationValue(
            ["embedding:ollamaEndpoint", "embedding:endpoint", "embedding:baseUrl", "embeddings:ollamaEndpoint", "embeddings:endpoint", "llm:baseUrl"],
            ["STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT", "STREAMINGDIGEST_EMBEDDING_ENDPOINT", "OLLAMA_BASE_URL", "OLLAMA_HOST"])
            ?? "http://localhost:11434";

    private string? ResolveConfigurationValue(IReadOnlyList<string> configurationKeys, IReadOnlyList<string> environmentVariables)
    {
        foreach (var key in configurationKeys)
        {
            var configuredValue = _configuration[key];
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                return configuredValue.Trim();
            }
        }

        foreach (var variable in environmentVariables)
        {
            var environmentValue = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue.Trim();
            }
        }

        return null;
    }

    private static Uri BuildUri(string endpoint, string relativePath)
    {
        var normalizedEndpoint = endpoint.Trim();
        if (!Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out var absoluteUri)
            || (!absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            if (!Uri.TryCreate($"http://{normalizedEndpoint}", UriKind.Absolute, out absoluteUri))
            {
                throw new InvalidOperationException($"The Ollama endpoint '{endpoint}' is not a valid absolute URI.");
            }
        }

        var builder = new UriBuilder(absoluteUri);
        var basePath = absoluteUri.AbsolutePath.TrimEnd('/');
        // Strip a trailing /api if the configured endpoint already points at the API root so we
        // don't double-up the path segment.
        if (basePath.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            basePath = basePath[..^"/api".Length];
        }

        builder.Path = string.IsNullOrWhiteSpace(basePath) ? $"/{relativePath}" : $"{basePath}/{relativePath}";
        return builder.Uri;
    }

    private sealed record OllamaPullRequest(string Model, bool Stream);

    private sealed record OllamaShowRequest(string Model);

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaTagModel>? Models { get; set; }
    }

    private sealed class OllamaTagModel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }
    }

    private sealed class OllamaPullProgressResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("total")]
        public long? Total { get; set; }

        [JsonPropertyName("completed")]
        public long? Completed { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class OllamaShowResponse
    {
        [JsonPropertyName("details")]
        public OllamaShowDetails? Details { get; set; }
    }

    // Ollama nests family/families (plus parameter_size, quantization_level, format,
    // parent_model) inside `details`; only the fields we consume are bound, the rest ride
    // along when Details is serialized into the opaque ModelRuntimeInfo.Details string.
    private sealed class OllamaShowDetails
    {
        [JsonPropertyName("family")]
        public string? Family { get; set; }

        [JsonPropertyName("families")]
        public List<string>? Families { get; set; }
    }
}
