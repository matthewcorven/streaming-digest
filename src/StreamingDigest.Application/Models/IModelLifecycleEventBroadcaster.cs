namespace StreamingDigest.Application.Models;

/// <summary>
/// In-process broadcaster for model-lifecycle events. Publishers (download job, verify
/// endpoint) call <see cref="Publish"/> once per persisted state change; each active SSE
/// subscriber receives exactly one ordered copy of every event published after it subscribed.
/// Events are not replayed to late subscribers — reconnecting clients reconcile through
/// <c>GET /api/models/status</c> per the implementation plan's SSE fallback rule (D5).
/// </summary>
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
