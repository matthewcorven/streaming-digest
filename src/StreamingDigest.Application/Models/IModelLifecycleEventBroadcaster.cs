namespace StreamingDigest.Application.Models;

/// <summary>
/// In-process broadcaster for model-lifecycle events. Publishers (download job, verify
/// endpoint) call <see cref="Publish"/> once per persisted state change; each active SSE
/// subscriber receives exactly one ordered copy of every event published after it subscribed.
/// Events are not replayed to late subscribers — reconnecting clients reconcile through
/// <c>GET /api/models/status</c> per the implementation plan's SSE fallback rule (D5).
/// </summary>
/// <remarks>
/// The broadcaster is deliberately <b>in-process only</b>. The API and the worker run as
/// separate processes, so events published inside the worker process never reach API-hosted
/// SSE subscribers. The cross-process source of truth is the persisted
/// <c>model_runtime_state</c> table together with the <c>GET /api/models/status</c> snapshot
/// endpoint (D5); SSE is a best-effort, same-process notification channel. A worker-side
/// publisher that resolves this interface in its own process will publish successfully but
/// reach no SSE clients — cross-process fan-out (e.g. Postgres LISTEN/NOTIFY bridging into
/// the API host) is an explicit follow-up if real-time worker events are required.
/// </remarks>
public interface IModelLifecycleEventBroadcaster
{
    /// <summary>
    /// Publishes one event to every currently subscribed SSE stream, preserving publish order
    /// per subscriber. A slow or faulted subscriber never blocks other subscribers.
    /// </summary>
    void Publish(ModelLifecycleEvent modelEvent);

    /// <summary>
    /// Streams events to one subscriber until the subscription is disposed or the caller's
    /// cancellation token is cancelled.
    /// </summary>
    IAsyncEnumerable<ModelLifecycleEvent> Subscribe(CancellationToken cancellationToken = default);
}
