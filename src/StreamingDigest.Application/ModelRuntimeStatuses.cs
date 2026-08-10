namespace StreamingDigest.Application;

/// <summary>
/// Canonical status values recorded in <c>model_runtime_state.status</c>. WS-2/WS-3/WS-4/WS-5
/// write these; the WS-7 guard reads them to project readiness.
/// </summary>
public static class ModelRuntimeStatuses
{
    public const string Ready = "ready";
    public const string Queued = "queued";
    public const string Downloading = "downloading";
    public const string Verifying = "verifying";
    public const string Missing = "missing";
    public const string Error = "error";
}
