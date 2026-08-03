using StreamingDigest.Application.Models;

namespace StreamingDigest.Application.Services;

/// <summary>
/// Client for interacting with a model runtime (e.g. Ollama).
/// Provides presence checks, streamed pulls, and model metadata.
/// </summary>
public interface IModelRuntimeClient
{
    /// <summary>
    /// Returns all models currently installed in the runtime (analogous to <c>ollama list</c>).
    /// </summary>
    Task<IReadOnlyList<InstalledModelInfo>> GetInstalledModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams pull progress for <paramref name="modelId"/> from the runtime.
    /// The sequence ends when a <see cref="PullProgress"/> with status <c>"success"</c> is yielded.
    /// </summary>
    IAsyncEnumerable<PullProgress> PullModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows detailed metadata for <paramref name="modelId"/> (analogous to <c>ollama show</c>).
    /// Returns <c>null</c> if the model is not installed.
    /// </summary>
    Task<ModelDetailInfo?> ShowModelAsync(string modelId, CancellationToken cancellationToken = default);
}