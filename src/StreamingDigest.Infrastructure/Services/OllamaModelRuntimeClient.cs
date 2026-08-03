using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Services;

namespace StreamingDigest.Infrastructure.Services;

/// <summary>
/// Ollama implementation of <see cref="IModelRuntimeClient"/>.
/// Communicates with the Ollama runtime over HTTP for model presence, pull, and show operations.
/// </summary>
public sealed class OllamaModelRuntimeClient : IModelRuntimeClient
{
    internal const string ProviderName = "ollama";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public OllamaModelRuntimeClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledModelInfo>> GetInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(JsonOptions, cancellationToken);
        if (body?.Models is null || body.Models.Count == 0)
        {
            return Array.Empty<InstalledModelInfo>();
        }

        return body.Models
            .Select(m => new InstalledModelInfo(
                m.Name,
                m.Digest,
                m.Size,
                m.ModifiedAt))
            .ToArray();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PullProgress> PullModelAsync(
        string modelId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var request = new OllamaPullRequest(modelId, true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pull")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var progress = JsonSerializer.Deserialize<PullProgress>(line, JsonOptions);
            if (progress is not null)
            {
                yield return progress;

                if (string.Equals(progress.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<ModelDetailInfo?> ShowModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var request = new OllamaShowRequest(modelId);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/show")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaShowResponse>(JsonOptions, cancellationToken);
        if (body is null)
        {
            return null;
        }

        return new ModelDetailInfo(
            body.Modelfile,
            body.Parameters,
            body.Template,
            body.DetailsJson);
    }

    // ── Request / Response DTOs ──────────────────────────────────────────

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaTagModel> Models);

    private sealed record OllamaTagModel(
        string Name,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("modified_at")] DateTimeOffset? ModifiedAt);

    private sealed record OllamaPullRequest(string Name, bool Stream);

    private sealed record OllamaShowRequest(string Name);

    private sealed class OllamaShowResponse
    {
        [JsonPropertyName("modelfile")]
        public string? Modelfile { get; set; }

        [JsonPropertyName("parameters")]
        public string? Parameters { get; set; }

        [JsonPropertyName("template")]
        public string? Template { get; set; }

        [JsonPropertyName("details")]
        public JsonElement? Details { get; set; }

        /// <summary>
        /// Serializes the <c>details</c> object to a JSON string if present.
        /// </summary>
        [JsonIgnore]
        public string? DetailsJson => Details?.ToString();
    }
}