using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace StreamingDigest.Application;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OllamaEmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var endpoint = ResolveEndpoint();
        var model = ResolveModel();
        var expectedDimensions = ResolveExpectedDimensions();

        var response = await _httpClient.PostAsJsonAsync(BuildEmbeddingUri(endpoint), new OllamaEmbeddingRequest(model, text), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ollama embedding request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty embedding response.");

        var embedding = payload.Embedding;
        if (embedding is null || embedding.Count == 0)
        {
            throw new InvalidOperationException("Ollama returned no embedding values.");
        }

        if (expectedDimensions is > 0 && embedding.Count != expectedDimensions)
        {
            throw new InvalidOperationException($"Ollama returned {embedding.Count} dimensions, but expected {expectedDimensions}.");
        }

        return new EmbeddingGenerationResult(payload.Model ?? model, embedding.Count, embedding);
    }

    private string ResolveEndpoint()
    {
        return ResolveConfigurationValue(
            ["embedding:ollamaEndpoint", "embedding:endpoint", "embedding:baseUrl", "embeddings:ollamaEndpoint", "embeddings:endpoint", "llm:baseUrl"],
            [
                "STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT",
                "STREAMINGDIGEST_EMBEDDING_ENDPOINT",
                "OLLAMA_BASE_URL",
                "OLLAMA_HOST"
            ])
            ?? "http://localhost:11434";
    }

    private string ResolveModel()
    {
        return ResolveConfigurationValue(
            ["embedding:model", "embeddings:model"],
            ["STREAMINGDIGEST_EMBEDDING_MODEL"])
            ?? "nomic-embed-text";
    }

    private int? ResolveExpectedDimensions()
    {
        var configured = ResolveConfigurationValue(
            ["embedding:expectedDimensions", "embedding:dimensions", "embeddings:expectedDimensions", "embeddings:dimensions"],
            ["STREAMINGDIGEST_EMBEDDING_EXPECTED_DIMENSIONS", "STREAMINGDIGEST_EMBEDDING_DIMENSIONS"]);

        return int.TryParse(configured, out var expectedDimensions) ? expectedDimensions : null;
    }

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

    private static Uri BuildEmbeddingUri(string endpoint)
    {
        var normalizedEndpoint = endpoint.Trim();
        if (Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out var absoluteUri)
            && IsHttpOrHttpsUri(absoluteUri))
        {
            return NormalizeAbsoluteUri(absoluteUri);
        }

        if (Uri.TryCreate($"http://{normalizedEndpoint}", UriKind.Absolute, out var inferredUri))
        {
            return NormalizeAbsoluteUri(inferredUri);
        }

        throw new InvalidOperationException($"The embedding endpoint '{endpoint}' is not a valid absolute URI.");
    }

    private static bool IsHttpOrHttpsUri(Uri uri)
        => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static Uri NormalizeAbsoluteUri(Uri absoluteUri)
    {
        if (!absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The embedding endpoint '{absoluteUri}' is not an http or https URI.");
        }

        var path = absoluteUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/api/embeddings", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/api/embed", StringComparison.OrdinalIgnoreCase))
        {
            return absoluteUri;
        }

        if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return new UriBuilder(absoluteUri) { Path = string.Concat(path, "/embeddings") }.Uri;
        }

        return new UriBuilder(absoluteUri) { Path = string.Concat(path, "/api/embeddings") }.Uri;
    }

    private sealed record OllamaEmbeddingRequest(string Model, string Input);

    private sealed class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public List<double>? Embedding { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }
}
