namespace StreamingDigest.Web.Models;

/// <summary>
/// Per-model row state in the Settings page model management UI. The transitions are
/// defined by <see cref="ModelRowViewModel"/> and tested independently in
/// <c>StreamingDigest.Web.UnitTests/ModelRowStateMachineTests.cs</c>.
/// </summary>
public enum ModelRowState
{
    /// <summary>Status not yet loaded or unknown from the runtime.</summary>
    Unknown,

    /// <summary>Download HTTP request is in-flight (POST /api/models/download).</summary>
    Submitting,

    /// <summary>202 accepted; download job is enqueued, waiting for running confirmation.</summary>
    Queued,

    /// <summary>Worker is actively pulling; progress% is populated.</summary>
    Running,

    /// <summary>Pull completed; a Verify step is needed before the model is marked ready.</summary>
    DownloadedNeedsVerify,

    /// <summary>Verify HTTP request is in-flight (POST /api/models/verify).</summary>
    Verifying,

    /// <summary>Model is present and has passed a verify probe.</summary>
    Ready,

    /// <summary>Download operation failed; user can retry.</summary>
    DownloadFailed,

    /// <summary>Verify probe failed or returned verified:false; user can retry.</summary>
    VerifyFailed,

    /// <summary>
    /// SSE stream dropped. The UI shows the last known badge from
    /// <see cref="ModelRowViewModel.StateBeforePause"/> overlaid with a "Live updates paused" indicator.
    /// A Refresh action reconciles state from GET /api/models/status.
    /// </summary>
    LiveUpdatesPaused
}
