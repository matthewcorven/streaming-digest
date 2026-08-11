namespace StreamingDigest.Web.Models;

/// <summary>
/// Per-model row view model for the Settings page. Encapsulates identity (read-only) plus
/// mutable state driven by the per-row state machine. State transitions are the only path
/// to mutating <see cref="RowState"/>; all other properties reflect the most-recently
/// reconciled runtime state.
/// </summary>
public sealed class ModelRowViewModel
{
    // ── Identity (immutable after construction) ──────────────────────────────────────────

    public string Id { get; }
    public string Label { get; }
    public string Provider { get; }
    public string Family { get; }
    public string RuntimeRole { get; }

    /// <summary>
    /// True for Ollama-provider models that can be pulled via the download pipeline.
    /// False for externally managed models (OpenAI, Whisper) — these expose Verify-only.
    /// </summary>
    public bool Downloadable { get; }

    // ── Mutable runtime state ─────────────────────────────────────────────────────────────

    public ModelRowState RowState { get; private set; } = ModelRowState.Unknown;

    /// <summary>
    /// The row state in effect immediately before an SSE drop. Used to render the
    /// last-known badge underneath the "Live updates paused" overlay.
    /// </summary>
    public ModelRowState StateBeforePause { get; private set; } = ModelRowState.Unknown;

    /// <summary>Download/pull progress percentage when <see cref="RowState"/> is Running.</summary>
    public int? ProgressPercent { get; private set; }

    /// <summary>Human-readable error populated on DownloadFailed or VerifyFailed.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Operation ID returned by the 202 download response, for status URL lookup.</summary>
    public Guid? CurrentOperationId { get; private set; }

    public ModelRowViewModel(
        string id,
        string label,
        string provider,
        string family,
        string runtimeRole,
        bool downloadable)
    {
        Id = id;
        Label = label;
        Provider = provider;
        Family = family;
        RuntimeRole = runtimeRole;
        Downloadable = downloadable;
    }

    // ── State transitions ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// User clicked "Download". Valid from Unknown, DownloadFailed, VerifyFailed.
    /// </summary>
    public bool TryBeginDownload()
    {
        if (RowState is not (ModelRowState.Unknown or ModelRowState.DownloadFailed or ModelRowState.VerifyFailed))
        {
            return false;
        }

        ErrorMessage = null;
        RowState = ModelRowState.Submitting;
        return true;
    }

    /// <summary>API returned 202 — download is durably enqueued.</summary>
    public void ApplyDownloadQueued(Guid operationId)
    {
        CurrentOperationId = operationId;
        ProgressPercent = null;
        ErrorMessage = null;
        RowState = ModelRowState.Queued;
    }

    /// <summary>SSE model.status=running (or status snapshot says running).</summary>
    public void ApplyRunning(int? progressPercent = null)
    {
        ProgressPercent = progressPercent;
        ErrorMessage = null;
        RowState = ModelRowState.Running;
    }

    /// <summary>
    /// Download pull completed per SSE or status snapshot (status = "ready" not yet verified
    /// but the pull itself finished). Transitions to DownloadedNeedsVerify so the user must
    /// explicitly verify before the row reaches Ready.
    /// </summary>
    public void ApplyDownloadCompleted()
    {
        ProgressPercent = null;
        ErrorMessage = null;
        RowState = ModelRowState.DownloadedNeedsVerify;
    }

    /// <summary>Download operation failed.</summary>
    public void ApplyDownloadFailed(string? errorMessage = null)
    {
        ProgressPercent = null;
        ErrorMessage = errorMessage;
        RowState = ModelRowState.DownloadFailed;
    }

    /// <summary>User clicked "Verify now" / "Re-verify" / "Retry verify".</summary>
    public bool TryBeginVerify()
    {
        if (RowState is not (
            ModelRowState.Unknown or
            ModelRowState.DownloadedNeedsVerify or
            ModelRowState.Ready or
            ModelRowState.VerifyFailed))
        {
            return false;
        }

        ErrorMessage = null;
        RowState = ModelRowState.Verifying;
        return true;
    }

    /// <summary>Verify returned verified:true.</summary>
    public void ApplyVerifyReady()
    {
        ErrorMessage = null;
        RowState = ModelRowState.Ready;
    }

    /// <summary>Verify returned verified:false or the request failed.</summary>
    public void ApplyVerifyFailed(string? errorMessage = null)
    {
        ErrorMessage = errorMessage;
        RowState = ModelRowState.VerifyFailed;
    }

    /// <summary>SSE stream dropped — capture the last known state as "before-pause".</summary>
    public void ApplySseDropped()
    {
        if (RowState == ModelRowState.LiveUpdatesPaused)
        {
            return;
        }

        StateBeforePause = RowState;
        RowState = ModelRowState.LiveUpdatesPaused;
    }

    /// <summary>
    /// Reconcile from a GET /api/models/status snapshot row. Maps the persisted
    /// <paramref name="status"/> string to the closest row state. Safe to call any time,
    /// including from LiveUpdatesPaused (reconnect reconcile).
    /// </summary>
    public void ApplyStatusSnapshot(string? status, int? progressPercent, string? lastErrorSummary)
    {
        ProgressPercent = progressPercent;
        ErrorMessage = lastErrorSummary;
        RowState = MapStatus(status);
        StateBeforePause = ModelRowState.Unknown;
    }

    // ── Rendering helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Badge text for the current row state.</summary>
    public string BadgeText => RowState switch
    {
        ModelRowState.Unknown => "Not checked",
        ModelRowState.Submitting => "Starting…",
        ModelRowState.Queued => "Queued",
        ModelRowState.Running => ProgressPercent.HasValue ? $"Downloading {ProgressPercent}%" : "Downloading…",
        ModelRowState.DownloadedNeedsVerify => "Downloaded",
        ModelRowState.Verifying => "Verifying",
        ModelRowState.Ready => "Ready",
        ModelRowState.DownloadFailed => "Download failed",
        ModelRowState.VerifyFailed => "Not ready",
        ModelRowState.LiveUpdatesPaused => StateBeforePause == ModelRowState.Unknown
            ? "Live updates paused"
            : $"{BadgeTextForState(StateBeforePause)} · Live updates paused",
        _ => RowState.ToString()
    };

    /// <summary>CSS modifier for the badge.</summary>
    public string BadgeCssModifier => RowState switch
    {
        ModelRowState.Ready => "status-success",
        ModelRowState.Running or ModelRowState.Queued or ModelRowState.Submitting => "status-info",
        ModelRowState.DownloadedNeedsVerify or ModelRowState.Verifying => "status-warning",
        ModelRowState.DownloadFailed or ModelRowState.VerifyFailed => "status-danger",
        ModelRowState.LiveUpdatesPaused => "status-neutral",
        _ => "status-neutral"
    };

    /// <summary>True when any user action should be blocked (in-flight request).</summary>
    public bool IsActionBusy =>
        RowState is ModelRowState.Submitting or ModelRowState.Verifying;

    /// <summary>
    /// True when the primary CTA for an Ollama model is "Download".
    /// Never shown for verify-only (external) models.
    /// </summary>
    public bool ShowDownloadCta =>
        Downloadable &&
        RowState is ModelRowState.Unknown or ModelRowState.DownloadFailed or ModelRowState.VerifyFailed;

    /// <summary>True when the retry-download CTA should be shown (after failure).</summary>
    public bool ShowRetryDownload =>
        Downloadable && RowState is ModelRowState.DownloadFailed;

    /// <summary>
    /// True when a Verify/Re-verify CTA should be shown. Always shown for external models
    /// that are not in a terminal downloading state.
    /// </summary>
    public bool ShowVerifyCta =>
        RowState is ModelRowState.Unknown or
                    ModelRowState.DownloadedNeedsVerify or
                    ModelRowState.Ready or
                    ModelRowState.VerifyFailed or
                    ModelRowState.LiveUpdatesPaused;

    /// <summary>Verify CTA label.</summary>
    public string VerifyCtaLabel => RowState switch
    {
        ModelRowState.Ready => "Re-verify",
        ModelRowState.VerifyFailed => "Retry verify",
        ModelRowState.LiveUpdatesPaused => "Verify",
        _ => "Verify now"
    };

    /// <summary>True when the Refresh secondary CTA should be shown.</summary>
    public bool ShowRefreshCta =>
        RowState is ModelRowState.Queued or
                    ModelRowState.Running or
                    ModelRowState.DownloadedNeedsVerify or
                    ModelRowState.Ready or
                    ModelRowState.DownloadFailed or
                    ModelRowState.VerifyFailed or
                    ModelRowState.LiveUpdatesPaused;

    // ── Private helpers ───────────────────────────────────────────────────────────────────

    private static ModelRowState MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "ready" => ModelRowState.Ready,
        "running" or "downloading" => ModelRowState.Running,
        "queued" => ModelRowState.Queued,
        "verifying" => ModelRowState.Verifying,
        "failed" or "error" => ModelRowState.DownloadFailed,
        _ => ModelRowState.Unknown
    };

    private static string BadgeTextForState(ModelRowState state) => state switch
    {
        ModelRowState.Unknown => "Not checked",
        ModelRowState.Submitting => "Starting…",
        ModelRowState.Queued => "Queued",
        ModelRowState.Running => "Downloading",
        ModelRowState.DownloadedNeedsVerify => "Downloaded",
        ModelRowState.Verifying => "Verifying",
        ModelRowState.Ready => "Ready",
        ModelRowState.DownloadFailed => "Download failed",
        ModelRowState.VerifyFailed => "Not ready",
        _ => state.ToString()
    };
}
