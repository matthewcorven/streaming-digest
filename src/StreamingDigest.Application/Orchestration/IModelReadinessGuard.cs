namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Interim model-readiness guard (Application-truth plan D7). The model-lifecycle plan
/// (seams S1–S7, issue #201) owns the real implementation; this minimal seam lets the
/// ingestion pipeline preflight model-consuming stages now and swap to the real guard
/// when #201 lands without changing call sites.
/// </summary>
public interface IModelReadinessGuard
{
    /// <summary>
    /// Returns <c>true</c> when the named model capability is ready to serve requests.
    /// Implementations must never throw — an unreachable runtime reports unready.
    /// </summary>
    /// <param name="capability">
    /// The model capability identifier (see <see cref="ModelCapabilities"/>).
    /// </param>
    Task<bool> IsReadyAsync(string capability, CancellationToken cancellationToken = default);
}

/// <summary>
/// Well-known model capabilities consumed by ingestion stages.
/// </summary>
public static class ModelCapabilities
{
    /// <summary>Local LLM used for segment refinement and link classification.</summary>
    public const string Llm = "llm";

    /// <summary>Embedding model used for search-document embeddings.</summary>
    public const string Embeddings = "embeddings";

    /// <summary>Whisper audio-to-text runtime for caption-less videos.</summary>
    public const string Whisper = "whisper";
}

/// <summary>
/// Minimal interim guard (Application-truth plan D7): reports every capability ready.
/// Used until the model-lifecycle plan lands the real guard; per-stage HTTP failures are
/// still caught by the stage handlers and degrade + notify, so no failure is silent.
/// </summary>
public sealed class InterimModelReadinessGuard : IModelReadinessGuard
{
    public Task<bool> IsReadyAsync(string capability, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
