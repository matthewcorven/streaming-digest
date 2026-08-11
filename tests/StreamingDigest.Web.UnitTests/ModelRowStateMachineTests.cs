using Xunit;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.UnitTests;

/// <summary>
/// Unit tests for the per-row state machine (WS-8). Covers the implemented transitions;
/// note that <see cref="ModelRowState.DownloadedNeedsVerify"/> from plan §7.1 is absent —
/// see the <see cref="ModelRowViewModel"/> class remarks for the documented deviation.
/// </summary>
public sealed class ModelRowStateMachineTests
{
    // ── Factory helpers ────────────────────────────────────────────────────────────────────

    private static ModelRowViewModel OllamaRow(string id = "bge-m3") =>
        new(id, "BAAI bge-m3", "ollama", "embedding", "embedding", downloadable: true);

    private static ModelRowViewModel ExternalRow(string id = "whisper") =>
        new(id, "Whisper Base", "whisper", "audio", "audio", downloadable: false);

    // ── Initial state ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void NewRow_StartsUnknown()
    {
        var row = OllamaRow();
        Assert.Equal(ModelRowState.Unknown, row.RowState);
    }

    [Fact]
    public void ExternalRow_StartsUnknown_NoDownloadCta()
    {
        var row = ExternalRow();
        Assert.Equal(ModelRowState.Unknown, row.RowState);
        Assert.False(row.Downloadable);
        Assert.False(row.ShowDownloadCta);
        Assert.True(row.ShowVerifyCta);
    }

    // ── Unknown → Submitting ───────────────────────────────────────────────────────────────

    [Fact]
    public void TryBeginDownload_FromUnknown_TransitionsToSubmitting()
    {
        var row = OllamaRow();
        var result = row.TryBeginDownload();
        Assert.True(result);
        Assert.Equal(ModelRowState.Submitting, row.RowState);
    }

    [Fact]
    public void TryBeginDownload_FromSubmitting_ReturnsFalseDoesNotTransition()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        var result = row.TryBeginDownload();
        Assert.False(result);
        Assert.Equal(ModelRowState.Submitting, row.RowState);
    }

    [Fact]
    public void TryBeginDownload_FromQueued_ReturnsFalse()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadQueued(Guid.NewGuid());
        var result = row.TryBeginDownload();
        Assert.False(result);
        Assert.Equal(ModelRowState.Queued, row.RowState);
    }

    [Fact]
    public void TryBeginDownload_FromRunning_ReturnsFalse()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadQueued(Guid.NewGuid());
        row.ApplyRunning(42);
        var result = row.TryBeginDownload();
        Assert.False(result);
        Assert.Equal(ModelRowState.Running, row.RowState);
    }

    [Fact]
    public void TryBeginDownload_ClearsErrorMessage()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadFailed("Previous error");
        row.TryBeginDownload();
        Assert.Null(row.ErrorMessage);
    }

    // ── Submitting → Queued ────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDownloadQueued_SetsOperationIdAndState()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        var opId = Guid.NewGuid();
        row.ApplyDownloadQueued(opId);
        Assert.Equal(ModelRowState.Queued, row.RowState);
        Assert.Equal(opId, row.CurrentOperationId);
        Assert.Null(row.ProgressPercent);
        Assert.Null(row.ErrorMessage);
    }

    // ── Queued / Submitting → Running ─────────────────────────────────────────────────────

    [Fact]
    public void ApplyRunning_SetsProgressPercent()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadQueued(Guid.NewGuid());
        row.ApplyRunning(37);
        Assert.Equal(ModelRowState.Running, row.RowState);
        Assert.Equal(37, row.ProgressPercent);
    }

    [Fact]
    public void ApplyRunning_WithNull_SetsRunningNoPercent()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyRunning(null);
        Assert.Equal(ModelRowState.Running, row.RowState);
        Assert.Null(row.ProgressPercent);
    }

    [Fact]
    public void BadgeText_Running_ShowsPercent()
    {
        var row = OllamaRow();
        row.ApplyRunning(75);
        Assert.Contains("75%", row.BadgeText);
    }

    [Fact]
    public void BadgeText_Running_NoPercent_ShowsDownloading()
    {
        var row = OllamaRow();
        row.ApplyRunning(null);
        Assert.Contains("Downloading", row.BadgeText);
    }

    // ── Running → DownloadFailed ───────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDownloadFailed_SetsErrorAndState()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyRunning(20);
        row.ApplyDownloadFailed("Connection timeout");
        Assert.Equal(ModelRowState.DownloadFailed, row.RowState);
        Assert.Equal("Connection timeout", row.ErrorMessage);
        Assert.Null(row.ProgressPercent);
    }

    [Fact]
    public void TryBeginDownload_FromDownloadFailed_Allowed()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadFailed("error");
        var result = row.TryBeginDownload();
        Assert.True(result);
        Assert.Equal(ModelRowState.Submitting, row.RowState);
    }

    // ── Verify transitions ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TryBeginVerify_FromUnknown_Allowed()
    {
        var row = OllamaRow();
        Assert.True(row.TryBeginVerify());
        Assert.Equal(ModelRowState.Verifying, row.RowState);
    }

    [Fact]
    public void TryBeginVerify_FromReady_Allowed_ReVerify()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyReady();
        Assert.True(row.TryBeginVerify());
        Assert.Equal(ModelRowState.Verifying, row.RowState);
    }

    [Fact]
    public void TryBeginVerify_FromVerifyFailed_Allowed()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyFailed("not ready");
        Assert.True(row.TryBeginVerify());
        Assert.Equal(ModelRowState.Verifying, row.RowState);
    }

    [Fact]
    public void TryBeginVerify_FromSubmitting_Blocked()
    {
        var row = OllamaRow();
        row.TryBeginDownload(); // Submitting
        Assert.False(row.TryBeginVerify());
        Assert.Equal(ModelRowState.Submitting, row.RowState);
    }

    [Fact]
    public void TryBeginVerify_FromQueued_Blocked()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadQueued(Guid.NewGuid());
        Assert.False(row.TryBeginVerify());
        Assert.Equal(ModelRowState.Queued, row.RowState);
    }

    [Fact]
    public void TryBeginVerify_FromRunning_Blocked()
    {
        var row = OllamaRow();
        row.ApplyRunning(50);
        Assert.False(row.TryBeginVerify());
    }

    // ── Verifying → Ready / VerifyFailed ──────────────────────────────────────────────────

    [Fact]
    public void ApplyVerifyReady_TransitionsToReady()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyReady();
        Assert.Equal(ModelRowState.Ready, row.RowState);
        Assert.Null(row.ErrorMessage);
    }

    [Fact]
    public void ApplyVerifyFailed_TransitionsToVerifyFailed()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyFailed("Model not found in /api/tags");
        Assert.Equal(ModelRowState.VerifyFailed, row.RowState);
        Assert.Equal("Model not found in /api/tags", row.ErrorMessage);
    }

    [Fact]
    public void TryBeginDownload_FromVerifyFailed_Allowed()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyFailed("not ready");
        Assert.True(row.TryBeginDownload());
        Assert.Equal(ModelRowState.Submitting, row.RowState);
    }

    // ── SSE drop / reconnect ────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplySseDropped_FromRunning_TransitionsToPaused_CapturesPriorState()
    {
        var row = OllamaRow();
        row.ApplyRunning(60);
        row.ApplySseDropped();
        Assert.Equal(ModelRowState.LiveUpdatesPaused, row.RowState);
        Assert.Equal(ModelRowState.Running, row.StateBeforePause);
    }

    [Fact]
    public void ApplySseDropped_FromQueued_TransitionsToPaused()
    {
        var row = OllamaRow();
        row.ApplyDownloadQueued(Guid.NewGuid());
        row.ApplySseDropped();
        Assert.Equal(ModelRowState.LiveUpdatesPaused, row.RowState);
        Assert.Equal(ModelRowState.Queued, row.StateBeforePause);
    }

    [Fact]
    public void ApplySseDropped_AlreadyPaused_IsIdempotent()
    {
        var row = OllamaRow();
        row.ApplyRunning(30);
        row.ApplySseDropped();
        row.ApplySseDropped();
        Assert.Equal(ModelRowState.LiveUpdatesPaused, row.RowState);
        Assert.Equal(ModelRowState.Running, row.StateBeforePause);
    }

    [Fact]
    public void ApplyStatusSnapshot_FromPaused_ReconcilesToNewState()
    {
        var row = OllamaRow();
        row.ApplyRunning(80);
        row.ApplySseDropped();
        row.ApplyStatusSnapshot("ready", null, null);
        Assert.Equal(ModelRowState.Ready, row.RowState);
        Assert.Equal(ModelRowState.Unknown, row.StateBeforePause);
    }

    // ── StatusSnapshot mapping ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ready", ModelRowState.Ready)]
    [InlineData("running", ModelRowState.Running)]
    [InlineData("downloading", ModelRowState.Running)]
    [InlineData("queued", ModelRowState.Queued)]
    [InlineData("verifying", ModelRowState.Verifying)]
    [InlineData("failed", ModelRowState.DownloadFailed)]
    [InlineData("error", ModelRowState.DownloadFailed)]
    [InlineData("unknown", ModelRowState.Unknown)]
    [InlineData(null, ModelRowState.Unknown)]
    [InlineData("", ModelRowState.Unknown)]
    [InlineData("READY", ModelRowState.Ready)]
    public void ApplyStatusSnapshot_MapsStatusCorrectly(string? status, ModelRowState expected)
    {
        var row = OllamaRow();
        row.ApplyStatusSnapshot(status, null, null);
        Assert.Equal(expected, row.RowState);
    }

    [Fact]
    public void ApplyStatusSnapshot_Running_SetsProgress()
    {
        var row = OllamaRow();
        row.ApplyStatusSnapshot("running", 45, null);
        Assert.Equal(45, row.ProgressPercent);
    }

    [Fact]
    public void ApplyStatusSnapshot_Failed_SetsError()
    {
        var row = OllamaRow();
        row.ApplyStatusSnapshot("failed", null, "Disk full");
        Assert.Equal("Disk full", row.ErrorMessage);
    }

    [Fact]
    public void ApplyStatusSnapshot_DoesNotStompInFlightSubmitting()
    {
        // Callers (ModelStatusService) skip in-flight rows; this tests the raw method
        // to confirm it DOES overwrite — the guard lives in the service, not the model.
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyStatusSnapshot("queued", null, null);
        Assert.Equal(ModelRowState.Queued, row.RowState);
    }

    // ── Rendering helpers ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BadgeText_Unknown_ShowsNotChecked()
    {
        var row = OllamaRow();
        Assert.Equal("Not checked", row.BadgeText);
    }

    [Fact]
    public void BadgeText_Ready_ShowsReady()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyReady();
        Assert.Equal("Ready", row.BadgeText);
    }

    [Fact]
    public void BadgeCssModifier_Ready_IsSuccess()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyReady();
        Assert.Equal("status-success", row.BadgeCssModifier);
    }

    [Fact]
    public void BadgeCssModifier_Failed_IsDanger()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadFailed("error");
        Assert.Equal("status-danger", row.BadgeCssModifier);
    }

    [Fact]
    public void IsActionBusy_Submitting_IsTrue()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        Assert.True(row.IsActionBusy);
    }

    [Fact]
    public void IsActionBusy_Verifying_IsTrue()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        Assert.True(row.IsActionBusy);
    }

    [Fact]
    public void IsActionBusy_Queued_IsFalse()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadQueued(Guid.NewGuid());
        Assert.False(row.IsActionBusy);
    }

    [Fact]
    public void ShowDownloadCta_OllamaUnknown_IsTrue()
    {
        var row = OllamaRow();
        Assert.True(row.ShowDownloadCta);
    }

    [Fact]
    public void ShowDownloadCta_ExternalModel_IsFalse()
    {
        var row = ExternalRow();
        Assert.False(row.ShowDownloadCta);
    }

    [Fact]
    public void ShowDownloadCta_Ready_IsFalse()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyReady();
        Assert.False(row.ShowDownloadCta);
    }

    [Fact]
    public void ShowRetryDownload_DownloadFailed_IsTrue()
    {
        var row = OllamaRow();
        row.TryBeginDownload();
        row.ApplyDownloadFailed("error");
        Assert.True(row.ShowRetryDownload);
    }

    [Fact]
    public void ShowRetryDownload_VerifyFailed_IsFalse()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyFailed("error");
        Assert.False(row.ShowRetryDownload);
    }

    [Fact]
    public void ShowVerifyCta_ExternalModel_Unknown_IsTrue()
    {
        var row = ExternalRow();
        Assert.True(row.ShowVerifyCta);
    }

    /// <summary>
    /// Fix 2 (adversarial review): Verify CTA must NOT render in LiveUpdatesPaused — §7.2
    /// specifies Refresh as the only CTA in that state, and TryBeginVerify rejects it
    /// (silent no-op). ShowVerifyCta must return false to prevent the orphaned button.
    /// </summary>
    [Fact]
    public void ShowVerifyCta_LiveUpdatesPaused_IsFalse()
    {
        var row = OllamaRow();
        row.ApplyRunning(50);
        row.ApplySseDropped();
        Assert.Equal(ModelRowState.LiveUpdatesPaused, row.RowState);
        Assert.False(row.ShowVerifyCta);
    }

    [Fact]
    public void TryBeginVerify_FromLiveUpdatesPaused_ReturnsFalse()
    {
        var row = OllamaRow();
        row.ApplyRunning(50);
        row.ApplySseDropped();
        Assert.False(row.TryBeginVerify());
        Assert.Equal(ModelRowState.LiveUpdatesPaused, row.RowState);
    }

    [Fact]
    public void VerifyCtaLabel_Ready_IsReVerify()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyReady();
        Assert.Equal("Re-verify", row.VerifyCtaLabel);
    }

    [Fact]
    public void VerifyCtaLabel_VerifyFailed_IsRetryVerify()
    {
        var row = OllamaRow();
        row.TryBeginVerify();
        row.ApplyVerifyFailed("err");
        Assert.Equal("Retry verify", row.VerifyCtaLabel);
    }

    [Fact]
    public void BadgeText_Paused_ContainsPausedLabel()
    {
        var row = OllamaRow();
        row.ApplyRunning(50);
        row.ApplySseDropped();
        Assert.Contains("paused", row.BadgeText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BadgeText_Paused_ContainsStateBeforePause()
    {
        var row = OllamaRow();
        row.ApplyRunning(50);
        row.ApplySseDropped();
        Assert.Contains("Downloading", row.BadgeText);
    }
}
