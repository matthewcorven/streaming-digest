using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Application;
using Testcontainers;
using Testcontainers.Ollama;
using Xunit;

namespace StreamingDigest.Integration.TestContainers;

/// <summary>
/// Wrapper around Testcontainers.Ollama providing lifecycle management and convenient access
/// to a disposable Ollama container instance for integration tests.
/// Implements IAsyncLifetime for xUnit fixture integration and IOllamaTestFixture for test access.
/// </summary>
public sealed class OllamaTestContainer : IAsyncLifetime, IOllamaTestFixture, IAsyncDisposable
{
    private const string DefaultImage = "ollama/ollama:latest";
    private const int OllamaDefaultPort = 11434;
    private const int HealthCheckRetries = 60;
    private const int HealthCheckDelayMs = 1000;

    private OllamaContainer? _container;
    private HttpClient? _httpClient;
    private Uri? _ollamaUri;

    /// <summary>
    /// Gets the HTTP endpoint URI for the running Ollama container.
    /// </summary>
    public Uri OllamaUri
    {
        get
        {
            if (_ollamaUri == null)
            {
                throw new InvalidOperationException("Ollama container has not been initialized. Call InitializeAsync first.");
            }

            return _ollamaUri;
        }
    }

    /// <summary>
    /// Gets or creates an HttpClient configured for the Ollama container endpoint.
    /// </summary>
    public HttpClient HttpClient
    {
        get
        {
            if (_httpClient == null)
            {
                _httpClient = new HttpClient { BaseAddress = OllamaUri };
            }

            return _httpClient;
        }
    }

    /// <summary>
    /// Initializes the Ollama container asynchronously (xUnit IAsyncLifetime.InitializeAsync).
    /// </summary>
    public async Task InitializeAsync()
    {
        _container = new OllamaBuilder(DefaultImage)
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();

        // OllamaContainer exposes port 11434 by default
        // Use GetConnectionString() or build URI from Container properties
        var connectionString = _container.GetConnectionString();
        _ollamaUri = new Uri(connectionString);

        Debug.WriteLine($"[OllamaTestContainer] Started at {_ollamaUri}");

        await WaitForReadyAsync();
    }

    /// <summary>
    /// Disposes the Ollama container asynchronously (xUnit IAsyncLifetime.DisposeAsync).
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_httpClient != null)
        {
            _httpClient.Dispose();
            _httpClient = null;
        }

        if (_container != null)
        {
            await _container.StopAsync();
            _container = null;
        }
    }

    /// <summary>
    /// Async disposable implementation for explicit cleanup.
    /// </summary>
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
    }

    /// <summary>
    /// Waits for the Ollama container to be ready by polling the health check endpoint.
    /// </summary>
    public async Task WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < HealthCheckRetries; attempt++)
        {
            try
            {
                using var response = await HttpClient.GetAsync("/api/tags", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[OllamaTestContainer] Health check passed on attempt {attempt + 1}");
                    return;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[OllamaTestContainer] Health check failed (attempt {attempt + 1}): {ex.Message}");
            }

            await Task.Delay(HealthCheckDelayMs, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Ollama container did not become ready at {OllamaUri} after {HealthCheckRetries * HealthCheckDelayMs / 1000} seconds.");
    }

    /// <summary>
    /// Pulls a model from the Ollama registry, yielding progress updates.
    /// </summary>
    public async IAsyncEnumerable<ModelPullProgress> PullModelAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));
        }

        var requestContent = new StringContent(
            JsonSerializer.Serialize(new { name = modelName }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await HttpClient.PostAsync(
            "/api/pull",
            requestContent,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to pull model '{modelName}': HTTP {response.StatusCode}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var progress = ParsePullProgress(line);
            if (progress != null)
            {
                yield return progress;
            }
        }
    }

    /// <summary>
    /// Lists all available models in the Ollama container.
    /// </summary>
    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync("/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDoc = JsonDocument.Parse(jsonContent);

        var models = new List<ModelInfo>();
        if (jsonDoc.RootElement.TryGetProperty("models", out var modelsElement)
            && modelsElement.ValueKind == JsonValueKind.Array)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

            foreach (var modelElement in modelsElement.EnumerateArray())
            {
                try
                {
                    var model = JsonSerializer.Deserialize<ModelInfo>(
                        modelElement.GetRawText(),
                        options);

                    if (model != null)
                    {
                        models.Add(model);
                    }
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"[OllamaTestContainer] Failed to deserialize model: {ex.Message}");
                }
            }
        }

        return models.AsReadOnly();
    }

    private static ModelPullProgress? ParsePullProgress(string line)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(line);
            if (!jsonDoc.RootElement.TryGetProperty("status", out var statusElement))
            {
                return null;
            }

            var status = statusElement.GetString();
            if (status == null)
            {
                return null;
            }

            long? total = null;
            if (jsonDoc.RootElement.TryGetProperty("total", out var totalElement) && totalElement.ValueKind == JsonValueKind.Number)
            {
                total = totalElement.GetInt64();
            }

            long? completed = null;
            if (jsonDoc.RootElement.TryGetProperty("completed", out var completedElement) && completedElement.ValueKind == JsonValueKind.Number)
            {
                completed = completedElement.GetInt64();
            }

            int? percent = null;
            if (jsonDoc.RootElement.TryGetProperty("percent", out var percentElement) && percentElement.ValueKind == JsonValueKind.Number)
            {
                percent = percentElement.GetInt32();
            }

            return new ModelPullProgress(status, total, completed, percent);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[OllamaTestContainer] Failed to parse pull progress: {ex.Message}");
            return null;
        }
    }
}
