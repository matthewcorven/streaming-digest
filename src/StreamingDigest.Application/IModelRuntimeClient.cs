namespace StreamingDigest.Application;

/// <summary>
/// Single management seam for a model runtime provider's presence, acquisition, and metadata
/// operations. In v1 the only implementation is Ollama (<c>/api/tags</c>, <c>/api/pull</c>,
/// <c>/api/show</c>); this interface stays a thin management client and never routes through
/// Semantic Kernel or Microsoft.Extensions.AI (acquisition is not inference).
/// </summary>
public interface IModelRuntimeClient
{
    /// <summary>
    /// Lists the models currently installed in the runtime (Ollama <c>GET /api/tags</c>).
    /// </summary>
    Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls (downloads) a model with streamed progress (Ollama <c>POST /api/pull</c> with
    /// <c>stream: true</c>). Each streamed line is yielded as a <see cref="ModelPullProgress"/>.
    /// The caller is responsible for translating progress into status transitions.
    /// </summary>
    IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns runtime metadata for an installed model (Ollama <c>POST /api/show</c>).
    /// </summary>
    Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default);
}

/// <summary>One model reported present by the runtime's tag list.</summary>
public sealed record ModelPresence(string Provider, string ModelId, string? Digest, long? SizeInBytes);

/// <summary>
/// One streamed progress event from a model pull. <see cref="Status"/> mirrors the Ollama
/// <c>status</c> field (e.g. <c>downloading</c>, <c>verifying sha256 digest</c>, <c>success</c>).
/// <see cref="Total"/>/<see cref="Completed"/> are byte counters when Ollama reports them.
/// <see cref="Percent"/> is derived when both counters are present and the total is positive.
/// </summary>
public sealed record ModelPullProgress(string Status, long? Total, long? Completed, int? Percent);

/// <summary>Runtime metadata for an installed model from <c>/api/show</c>.</summary>
public sealed record ModelRuntimeInfo(string Provider, string ModelId, string? Details, IReadOnlyList<string> Families);
