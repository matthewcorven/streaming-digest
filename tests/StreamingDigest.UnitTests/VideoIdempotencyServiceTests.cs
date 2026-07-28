using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class VideoIdempotencyServiceTests
{
    // ── Unavailable ───────────────────────────────────────────────────────────

    [Fact]
    public void ClassifySkipReason_returns_Unavailable_for_unavailable_status()
    {
        var result = VideoIdempotencyService.ClassifySkipReason(IngestionStatuses.Unavailable);
        Assert.Equal(VideoSkipReason.Unavailable, result);
    }

    [Fact]
    public void ClassifySkipReason_returns_Unavailable_even_when_reprocess_requested()
    {
        // Unavailable is an unconditional terminal state — reprocess does not apply
        var result = VideoIdempotencyService.ClassifySkipReason(IngestionStatuses.Unavailable, isReprocessRequest: true);
        Assert.Equal(VideoSkipReason.Unavailable, result);
    }

    // ── Already processed (idempotency guard) ────────────────────────────────

    [Theory]
    [InlineData(IngestionStatuses.Processed)]
    [InlineData(IngestionStatuses.ProcessedWithWarnings)]
    public void ClassifySkipReason_returns_AlreadyProcessed_for_terminal_success_without_reprocess(string status)
    {
        var result = VideoIdempotencyService.ClassifySkipReason(status, isReprocessRequest: false);
        Assert.Equal(VideoSkipReason.AlreadyProcessed, result);
    }

    [Theory]
    [InlineData(IngestionStatuses.Processed)]
    [InlineData(IngestionStatuses.ProcessedWithWarnings)]
    public void ClassifySkipReason_returns_None_for_terminal_success_when_reprocess_requested(string status)
    {
        // Reprocess bypasses the idempotency guard
        var result = VideoIdempotencyService.ClassifySkipReason(status, isReprocessRequest: true);
        Assert.Equal(VideoSkipReason.None, result);
    }

    // ── Non-terminal / retryable ──────────────────────────────────────────────

    [Theory]
    [InlineData(IngestionStatuses.Pending)]
    [InlineData(IngestionStatuses.Processing)]
    [InlineData(IngestionStatuses.Failed)]
    [InlineData(IngestionStatuses.Skipped)]
    public void ClassifySkipReason_returns_None_for_non_terminal_statuses(string status)
    {
        var result = VideoIdempotencyService.ClassifySkipReason(status);
        Assert.Equal(VideoSkipReason.None, result);
    }

    [Theory]
    [InlineData(IngestionStatuses.Pending)]
    [InlineData(IngestionStatuses.Processing)]
    [InlineData(IngestionStatuses.Failed)]
    [InlineData(IngestionStatuses.Skipped)]
    public void ClassifySkipReason_returns_None_for_non_terminal_statuses_even_when_reprocess_requested(string status)
    {
        var result = VideoIdempotencyService.ClassifySkipReason(status, isReprocessRequest: true);
        Assert.Equal(VideoSkipReason.None, result);
    }

    // ── Null / unknown status (new video not yet in DB) ───────────────────────

    [Fact]
    public void ClassifySkipReason_returns_None_when_status_is_null()
    {
        var result = VideoIdempotencyService.ClassifySkipReason(null);
        Assert.Equal(VideoSkipReason.None, result);
    }

    [Fact]
    public void ClassifySkipReason_returns_None_when_status_is_empty()
    {
        var result = VideoIdempotencyService.ClassifySkipReason(string.Empty);
        Assert.Equal(VideoSkipReason.None, result);
    }

    // ── Default parameter ─────────────────────────────────────────────────────

    [Fact]
    public void ClassifySkipReason_defaults_isReprocessRequest_to_false()
    {
        // Calling without the second parameter should behave as isReprocessRequest: false
        Assert.Equal(
            VideoIdempotencyService.ClassifySkipReason(IngestionStatuses.Processed, isReprocessRequest: false),
            VideoIdempotencyService.ClassifySkipReason(IngestionStatuses.Processed));
    }
}
