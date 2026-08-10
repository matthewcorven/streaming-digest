using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// Shared seam guard (Model Lifecycle plan §8, WS-7) answering "is model X for role Y
/// ready right now?" from <c>model_runtime_state</c>. Every runtime seam S1–S7 (embedding
/// indexing, query embedding, cluster build, transcript refine, link classify, whisper,
/// admin embedding test) routes its preflight readiness check through this guard instead
/// of re-implementing its own probe.
/// </summary>
public interface IModelReadinessGuard
{
    /// <summary>
    /// Returns the readiness of the currently-configured model for <paramref name="role"/>.
    /// Never throws for an unready model — an unready model is a normal, expected outcome
    /// that seams degrade from; only infrastructure faults propagate.
    /// </summary>
    Task<ModelReadiness> CheckAsync(RuntimeRole role, CancellationToken cancellationToken = default);
}

/// <summary>
/// Readiness of the model that serves a <see cref="RuntimeRole"/> right now.
/// </summary>
/// <param name="Role">The role that was checked.</param>
/// <param name="Provider">The provider of the resolved model (e.g. ollama, whisper).</param>
/// <param name="ModelId">The resolved model id currently configured for the role.</param>
/// <param name="IsReady"><c>true</c> only when the model's recorded status is ready.</param>
/// <param name="Status">The raw recorded status (e.g. ready, missing, queued, downloading, error).</param>
/// <param name="Reason">Human-readable explanation used in notifications / degrade messages.</param>
public sealed record ModelReadiness(
    RuntimeRole Role,
    string Provider,
    string ModelId,
    bool IsReady,
    string? Status,
    string Reason);
