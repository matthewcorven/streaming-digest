using StreamingDigest.Application;

namespace StreamingDigest.Integration.TestContainers;

/// <summary>
/// Fixture interface for managing an Ollama test container instance.
/// Provides common operations for model management and HTTP access in integration tests.
/// </summary>
public interface IOllamaTestFixture
{
    /// <summary>
    /// Gets the HTTP endpoint URI for the running Ollama container.
    /// </summary>
    Uri OllamaUri { get; }

    /// <summary>
    /// Gets or creates an HttpClient preconfigured for the Ollama container.
    /// </summary>
    HttpClient HttpClient { get; }

    /// <summary>
    /// Pulls a model from the Ollama registry asynchronously.
    /// </summary>
    /// <param name="modelName">The model name/identifier (e.g., "qwen2.5:0.5b").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of pull progress updates.</returns>
    IAsyncEnumerable<ModelPullProgress> PullModelAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for the Ollama container to be ready (health check passes).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task WaitForReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all models currently available in the Ollama container.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of available models.</returns>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// DTO for model information.
/// </summary>
public sealed class ModelInfo
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public required DateTime ModifiedAt { get; init; }
    public required long Size { get; init; }
    public required string Digest { get; init; }
    public string? Details { get; init; }
}
