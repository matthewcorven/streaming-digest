namespace StreamingDigest.Application.Models;

/// <summary>
/// A durable handoff record for a single model download request. The API persists the
/// operation + model runtime state, enqueues a Hangfire job carrying this command, and the
/// worker relays it into the bounded execution channel that runs the real streamed pull.
/// </summary>
public sealed record ModelDownloadCommand(
    Guid OperationId,
    string Provider,
    string ModelId,
    string RuntimeRole,
    DateTimeOffset RequestedAtUtc);

/// <summary>Canonical <c>model_runtime_state.status</c> values written by the download pipeline.</summary>
public static class ModelDownloadStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
