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
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty embedding response.");

        var embedding = payload.Embedding ?? payload.Embeddings?.FirstOrDefault();
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
        return _configuration["embedding:ollamaEndpoint"]
            ?? _configuration["embedding:baseUrl"]
            ?? _configuration["embeddings:ollamaEndpoint"]
            ?? _configuration["llm:baseUrl"]
            ?? Environment.GetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_HOST")
            ?? "http://localhost:11434";
    }

    private string ResolveModel()
    {
        return _configuration["embedding:model"]
            ?? _configuration["embeddings:model"]
            ?? Environment.GetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_MODEL")
            ?? "nomic-embed-text";
    }

    private int? ResolveExpectedDimensions()
    {
        var configured = _configuration["embedding:expectedDimensions"]
            ?? _configuration["embeddings:expectedDimensions"]
            ?? Environment.GetEnvironmentVariable("STREAMINGDIGEST_EMBEDDING_EXPECTED_DIMENSIONS");

        return int.TryParse(configured, out var expectedDimensions) ? expectedDimensions : null;
    }

    private static Uri BuildEmbeddingUri(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
        {
            var path = absoluteUri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/api/embeddings", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/api/embed", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri;
            }

            return new Uri(absoluteUri, "/api/embeddings");
        }

        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The embedding endpoint '{endpoint}' is not a valid absolute URI.");
        }

        return new Uri(endpoint.TrimEnd('/') + "/api/embeddings", UriKind.Absolute);
    }

    private sealed record OllamaEmbeddingRequest(string Model, string Input);

    private sealed class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public List<double>? Embedding { get; set; }

        [JsonPropertyName("embeddings")]
        public List<List<double>>? Embeddings { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }
}
