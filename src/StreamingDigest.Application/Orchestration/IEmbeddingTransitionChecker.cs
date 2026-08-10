namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Reports the state of any in-flight embedding-regeneration operation (ADR-0011).
/// The scheduled ingestion job checks this seam before firing: when a transition is
/// active the run is skipped; when the transition completes the single catch-up run
/// is enqueued.
/// </summary>
public interface IEmbeddingTransitionChecker
{
    /// <summary>
    /// Returns <c>true</c> when an embedding-regeneration operation is currently
    /// <c>queued</c> or <c>running</c>.
    /// </summary>
    Task<bool> IsTransitionActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a snapshot of the most-recently-completed embedding-regeneration
    /// operation, or <c>null</c> when none exists.
    /// </summary>
    Task<EmbeddingTransitionSnapshot?> GetLastCompletedTransitionAsync(CancellationToken cancellationToken = default);
}

/// <summary>A point-in-time read of the most-recently-completed embedding transition.</summary>
/// <param name="CompletedAt">When the operation transitioned to <c>completed</c>.</param>
public sealed record EmbeddingTransitionSnapshot(DateTimeOffset CompletedAt);
