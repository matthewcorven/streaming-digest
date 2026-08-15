namespace StreamingDigest.Web.Models;

/// <summary>
/// Per-model row view model for the Settings page. Encapsulates identity (read-only) plus
/// mutable state driven by the per-row state machine. State transitions are the only path
/// to mutating <see cref="RowState"/>; all other properties reflect the most-recently
/// reconciled runtime state.
/// </summary>
/// <remarks>
/// <b>Deviation from plan §7.1</b>: The <c>DownloadedNeedsVerify</c> state described in
/// plan §7.1 is not present in this implementation. The server worker confirms model
/// presence via <c>GET /api/tags</c> immediately after the pull and only writes
/// <c>model_runtime_state.status = "ready"</c> if the tag is present. There is therefore
/// no intermediate "downloaded-but-not-yet-verified" status value that the status endpoint
/// can emit. <c>GET /api/models/status</c> transitions directly from <c>running</c> →
/// <c>ready</c>; a separate client-side verify step is not required and the state is
/// unreachable in production. Users may still issue a manual re-verify from <c>Ready</c>.
/// </remarks>
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

    /// <summary>
    /// True when the settings UI can perform a truthful local verify probe.
    /// Unsupported external providers stay informational only.
    /// </summary>
    public bool SupportsVerify { get; }

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
        bool downloadable,
        bool supportsVerify = true)
    {
        Id = id;
        Label = label;
        Provider = provider;
        Family = family;
        RuntimeRole = runtimeRole;
        Downloadable = downloadable;
        SupportsVerify = supportsVerify;
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
    public void ApplyStatusSnapshot(string? status, int? progressPercent, string? lastErrorSummary, string? detailsJson = null)
    {
        if (!SupportsVerify && status is not null && (status.Equals("failed", StringComparison.OrdinalIgnoreCase) || status.Equals("error", StringComparison.OrdinalIgnoreCase)))
        {
            ProgressPercent = null;
            ErrorMessage = null;
            RowState = ModelRowState.Unknown;
            StateBeforePause = ModelRowState.Unknown;
            return;
        }

        ProgressPercent = progressPercent;
        ErrorMessage = lastErrorSummary;
        RowState = MapStatus(status, detailsJson);
        StateBeforePause = ModelRowState.Unknown;
    }

    // ── Rendering helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Badge text for the current row state.</summary>
    public string BadgeText => RowState switch
    {
        ModelRowState.Unknown => "Not checked",
        ModelRowState.Submitting => "Starting…",
        // "Queued for download" matches plan §7.3 honest-ack copy requirement.
        ModelRowState.Queued => "Queued for download",
        ModelRowState.Running => ProgressPercent.HasValue ? $"Downloading {ProgressPercent}%" : "Downloading…",
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
        ModelRowState.Verifying => "status-warning",
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
    /// Note: <see cref="ModelRowState.LiveUpdatesPaused"/> is intentionally excluded — §7.2
    /// specifies Refresh as the only CTA in that state, and <see cref="TryBeginVerify"/>
    /// rejects verify from paused (which would cause a silent no-op if the button were shown).
    /// </summary>
    public bool ShowVerifyCta =>
        SupportsVerify &&
        RowState is ModelRowState.Unknown or
                    ModelRowState.Ready or
                    ModelRowState.VerifyFailed;

    /// <summary>Verify CTA label.</summary>
    public string VerifyCtaLabel => RowState switch
    {
        ModelRowState.Ready => "Re-verify",
        ModelRowState.VerifyFailed => "Retry verify",
        _ => "Verify now"
    };

    /// <summary>True when the Refresh secondary CTA should be shown.</summary>
    public bool ShowRefreshCta =>
        RowState is ModelRowState.Queued or
                    ModelRowState.Running or
                    ModelRowState.Ready or
                    ModelRowState.DownloadFailed or
                    ModelRowState.VerifyFailed or
                    ModelRowState.LiveUpdatesPaused;

    // ── Private helpers ───────────────────────────────────────────────────────────────────

    private ModelRowState MapStatus(string? status, string? detailsJson) => status?.ToLowerInvariant() switch
    {
        "ready" => ModelRowState.Ready,
        "running" or "downloading" => ModelRowState.Running,
        "queued" => ModelRowState.Queued,
        "verifying" => ModelRowState.Verifying,
        "verify-failed" => ModelRowState.VerifyFailed,
        "failed" or "error" => IsVerifyFailureSnapshot(detailsJson) ? ModelRowState.VerifyFailed : ModelRowState.DownloadFailed,
        _ => ModelRowState.Unknown
    };

    private bool IsVerifyFailureSnapshot(string? detailsJson)
    {
        if (RowState is ModelRowState.Verifying or ModelRowState.VerifyFailed)
        {
            return true;
        }

        if (!Downloadable)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return false;
        }

        try
        {
            using var details = System.Text.Json.JsonDocument.Parse(detailsJson);
            return details.RootElement.TryGetProperty("probe", out var probe)
                && string.Equals(probe.GetString(), "verify", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string BadgeTextForState(ModelRowState state) => state switch
    {
        ModelRowState.Unknown => "Not checked",
        ModelRowState.Submitting => "Starting…",
        ModelRowState.Queued => "Queued for download",
        ModelRowState.Running => "Downloading",
        ModelRowState.Verifying => "Verifying",
        ModelRowState.Ready => "Ready",
        ModelRowState.DownloadFailed => "Download failed",
        ModelRowState.VerifyFailed => "Not ready",
        _ => state.ToString()
    };
}
