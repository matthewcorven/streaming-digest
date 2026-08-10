namespace StreamingDigest.Application.Models;

/// <summary>
/// One model-lifecycle event delivered over the SSE stream (<c>GET /api/models/events</c>).
/// <see cref="Name"/> is the SSE <c>event:</c> field (e.g. <c>model.status</c>,
/// <c>operation.status</c>, <c>operation.completed</c>, <c>operation.failed</c>) and
/// <see cref="DataJson"/> is a pre-serialized JSON payload for the SSE <c>data:</c> field.
/// </summary>
public sealed record ModelLifecycleEvent(string Name, string DataJson, DateTimeOffset Timestamp);
