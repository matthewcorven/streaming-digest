using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// A2 orchestrator (Application-truth epic #207, issue #211): owns the channel-run
/// sequence and the per-video guarded pipeline (plan §5.1/§5.2, ARCHITECTURE §4.1–4.4).
/// Every model-consuming stage preflights through <see cref="IModelReadinessGuard"/>;
/// unready capabilities take the existing deterministic/heuristic fallback (or defer
/// embeddings) and emit exactly one notification event — never a silent 500, never a
/// silent success.
/// </summary>
public interface IIngestionOrchestrator
{
    /// <summary>
    /// Runs one channel ingestion: resolve recent videos → filter → idempotency skip →
    /// create run + items → per-video stages (bounded concurrency) → finalize run
    /// counters and terminal status.
    /// </summary>
    Task<IngestionRun> RunChannelIngestionAsync(
        ChannelIngestionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ChannelIngestionRequest
{
    public required Guid ChannelId { get; init; }

    /// <summary>Run type label (e.g. <c>scheduled</c>, <c>manual</c>, <c>backfill</c>).</summary>
    public string RunType { get; init; } = "manual";

    /// <summary>Who/what triggered the run (e.g. <c>system</c>, a user id).</summary>
    public string TriggeredBy { get; init; } = "system";

    /// <summary>
    /// When <c>true</c>, videos with a terminal-success status are re-processed instead
    /// of skipped by the idempotency guard.
    /// </summary>
    public bool IsReprocessRequest { get; init; }

    /// <summary>Optional operation record to correlate the run with.</summary>
    public Guid? OperationId { get; init; }

    /// <summary>Max videos processed concurrently within this run (bounded concurrency).</summary>
    public int MaxVideoConcurrency { get; init; } = 2;

    /// <summary>
    /// Optional Matrix room or notification target to route the digest notification to.
    /// When <c>null</c>, the dispatch service uses its configured default target.
    /// </summary>
    public string? NotificationTarget { get; init; }
}
