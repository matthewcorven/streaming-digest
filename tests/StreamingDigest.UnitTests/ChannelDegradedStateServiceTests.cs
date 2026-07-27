using StreamingDigest.Application;
using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class ChannelDegradedStateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static Channel MakeChannel(bool isDegraded = false, bool isPaused = false, int consecutiveFailures = 0)
        => new()
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UCtest123",
            NameOriginal = "Test Channel",
            ProfileUrl = "https://www.youtube.com/channel/UCtest123",
            IsDegraded = isDegraded,
            IsPaused = isPaused,
            ConsecutiveFailures = consecutiveFailures
        };

    // ── ShouldSkipForFullIngestion / ShouldProbe ──────────────────────────────

    [Fact]
    public void ShouldSkipForFullIngestion_returns_false_for_healthy_channel()
    {
        var channel = MakeChannel(isDegraded: false);
        Assert.False(ChannelDegradedStateService.ShouldSkipForFullIngestion(channel));
    }

    [Fact]
    public void ShouldSkipForFullIngestion_returns_true_for_degraded_non_paused_channel()
    {
        var channel = MakeChannel(isDegraded: true, isPaused: false);
        Assert.True(ChannelDegradedStateService.ShouldSkipForFullIngestion(channel));
    }

    [Fact]
    public void ShouldSkipForFullIngestion_returns_false_for_degraded_but_paused_channel()
    {
        // Paused channels get no selection, no probing, and no failure counting
        var channel = MakeChannel(isDegraded: true, isPaused: true);
        Assert.False(ChannelDegradedStateService.ShouldSkipForFullIngestion(channel));
    }

    [Fact]
    public void ShouldProbe_mirrors_ShouldSkipForFullIngestion()
    {
        // The same channels that skip full ingestion receive exactly one probe
        foreach (var (isDegraded, isPaused) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var channel = MakeChannel(isDegraded, isPaused);
            Assert.Equal(
                ChannelDegradedStateService.ShouldSkipForFullIngestion(channel),
                ChannelDegradedStateService.ShouldProbe(channel));
        }
    }

    // ── RecordAdapterFailure — deferment pauses counter ───────────────────────

    [Fact]
    public void RecordAdapterFailure_returns_CounterPausedByDeferment_when_deferment_active()
    {
        var channel = MakeChannel(consecutiveFailures: 0);
        var transition = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: true, Now);

        Assert.Equal(ChannelDegradedTransition.CounterPausedByDeferment, transition);
        Assert.Equal(0, channel.ConsecutiveFailures); // counter not incremented
        Assert.False(channel.IsDegraded);
    }

    [Fact]
    public void RecordAdapterFailure_does_not_modify_channel_when_deferment_active()
    {
        var channel = MakeChannel(consecutiveFailures: 1);
        var before = channel.ConsecutiveFailures;

        ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: true, Now);

        Assert.Equal(before, channel.ConsecutiveFailures);
        Assert.False(channel.IsDegraded);
    }

    // ── RecordAdapterFailure — incrementing below threshold ───────────────────

    [Fact]
    public void RecordAdapterFailure_increments_counter_on_first_failure()
    {
        var channel = MakeChannel(consecutiveFailures: 0);
        var transition = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now);

        Assert.Equal(ChannelDegradedTransition.ConsecutiveFailureIncremented, transition);
        Assert.Equal(1, channel.ConsecutiveFailures);
        Assert.False(channel.IsDegraded);
    }

    // ── RecordAdapterFailure — entering degraded at threshold ─────────────────

    [Fact]
    public void RecordAdapterFailure_enters_degraded_on_second_failure()
    {
        var channel = MakeChannel(consecutiveFailures: ChannelDegradedStateService.DegradedFailureThreshold - 1);
        var transition = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now);

        Assert.Equal(ChannelDegradedTransition.DegradedEntered, transition);
        Assert.True(channel.IsDegraded);
        Assert.Equal(Now, channel.DegradedAt);
        Assert.Equal(ChannelDegradedStateService.DegradedFailureThreshold, channel.ConsecutiveFailures);
    }

    [Fact]
    public void RecordAdapterFailure_sets_degraded_at_timestamp_on_entry()
    {
        var channel = MakeChannel(consecutiveFailures: 1);
        var specificNow = new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

        ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, specificNow);

        Assert.Equal(specificNow, channel.DegradedAt);
    }

    [Fact]
    public void RecordAdapterFailure_threshold_is_two()
    {
        Assert.Equal(2, ChannelDegradedStateService.DegradedFailureThreshold);
    }

    // ── RecordAdapterFailure — already degraded ───────────────────────────────

    [Fact]
    public void RecordAdapterFailure_returns_None_when_channel_already_degraded()
    {
        var channel = MakeChannel(isDegraded: true, consecutiveFailures: 5);
        var originalDegradedAt = Now.AddDays(-1);
        channel.DegradedAt = originalDegradedAt;

        var transition = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now);

        Assert.Equal(ChannelDegradedTransition.None, transition);
        Assert.True(channel.IsDegraded);
        Assert.Equal(6, channel.ConsecutiveFailures); // still increments
        Assert.Equal(originalDegradedAt, channel.DegradedAt); // not overwritten
    }

    // ── RecordSuccessfulProbe ────────────────────────────────────────────────

    [Fact]
    public void RecordSuccessfulProbe_clears_degraded_state()
    {
        var channel = MakeChannel(isDegraded: true, consecutiveFailures: 4);
        channel.DegradedAt = Now.AddDays(-3);

        ChannelDegradedStateService.RecordSuccessfulProbe(channel, Now);

        Assert.False(channel.IsDegraded);
        Assert.Equal(0, channel.ConsecutiveFailures);
        Assert.Null(channel.DegradedAt);
        Assert.Equal(Now, channel.LastProbeAt);
    }

    [Fact]
    public void RecordSuccessfulProbe_updates_last_probe_at()
    {
        var channel = MakeChannel(isDegraded: true);
        var probeTime = new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero);

        ChannelDegradedStateService.RecordSuccessfulProbe(channel, probeTime);

        Assert.Equal(probeTime, channel.LastProbeAt);
    }

    // ── RecordFailedProbe ────────────────────────────────────────────────────

    [Fact]
    public void RecordFailedProbe_increments_failure_count()
    {
        var channel = MakeChannel(isDegraded: true, consecutiveFailures: 2);
        var originalDegradedAt = Now.AddDays(-1);
        channel.DegradedAt = originalDegradedAt;

        ChannelDegradedStateService.RecordFailedProbe(channel, Now);

        Assert.Equal(3, channel.ConsecutiveFailures);
        Assert.True(channel.IsDegraded); // still degraded
        Assert.Equal(originalDegradedAt, channel.DegradedAt); // not modified
        Assert.Equal(Now, channel.LastProbeAt);
    }

    [Fact]
    public void RecordFailedProbe_updates_last_probe_at()
    {
        var channel = MakeChannel(isDegraded: true);
        var probeTime = new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero);

        ChannelDegradedStateService.RecordFailedProbe(channel, probeTime);

        Assert.Equal(probeTime, channel.LastProbeAt);
    }

    // ── ClearDegradedManually ────────────────────────────────────────────────

    [Fact]
    public void ClearDegradedManually_clears_degraded_state_and_resets_counter()
    {
        var channel = MakeChannel(isDegraded: true, consecutiveFailures: 7);
        channel.DegradedAt = Now.AddDays(-5);

        ChannelDegradedStateService.ClearDegradedManually(channel, Now);

        Assert.False(channel.IsDegraded);
        Assert.Equal(0, channel.ConsecutiveFailures);
        Assert.Null(channel.DegradedAt);
    }

    [Fact]
    public void ClearDegradedManually_does_not_update_last_probe_at()
    {
        var channel = MakeChannel(isDegraded: true);
        var originalProbeAt = Now.AddDays(-1);
        channel.LastProbeAt = originalProbeAt;

        ChannelDegradedStateService.ClearDegradedManually(channel, Now);

        Assert.Equal(originalProbeAt, channel.LastProbeAt); // unchanged
    }

    // ── Full lifecycle scenario ───────────────────────────────────────────────

    [Fact]
    public void Full_lifecycle_enter_degraded_then_clear_via_successful_probe()
    {
        var channel = MakeChannel(consecutiveFailures: 0);

        // Run 1 fails
        var t1 = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now);
        Assert.Equal(ChannelDegradedTransition.ConsecutiveFailureIncremented, t1);
        Assert.False(channel.IsDegraded);

        // Run 2 fails — enters degraded
        var t2 = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now.AddHours(24));
        Assert.Equal(ChannelDegradedTransition.DegradedEntered, t2);
        Assert.True(channel.IsDegraded);

        // Degraded channel should skip full ingestion and probe instead
        Assert.True(ChannelDegradedStateService.ShouldSkipForFullIngestion(channel));
        Assert.True(ChannelDegradedStateService.ShouldProbe(channel));

        // Probe fails — stays degraded
        ChannelDegradedStateService.RecordFailedProbe(channel, Now.AddHours(48));
        Assert.True(channel.IsDegraded);
        Assert.Equal(3, channel.ConsecutiveFailures);

        // Probe succeeds — clears degraded
        ChannelDegradedStateService.RecordSuccessfulProbe(channel, Now.AddHours(72));
        Assert.False(channel.IsDegraded);
        Assert.Equal(0, channel.ConsecutiveFailures);
        Assert.Null(channel.DegradedAt);

        // Channel is now active again
        Assert.False(ChannelDegradedStateService.ShouldSkipForFullIngestion(channel));
        Assert.False(ChannelDegradedStateService.ShouldProbe(channel));
    }

    [Fact]
    public void Full_lifecycle_deferment_pauses_counter_then_failures_resume()
    {
        var channel = MakeChannel(consecutiveFailures: 0);

        // Run 1 fails — first real failure
        ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now);
        Assert.Equal(1, channel.ConsecutiveFailures);

        // Run 2: deferment is active — counter paused, channel should NOT degrade
        var t = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: true, Now.AddHours(24));
        Assert.Equal(ChannelDegradedTransition.CounterPausedByDeferment, t);
        Assert.Equal(1, channel.ConsecutiveFailures); // unchanged
        Assert.False(channel.IsDegraded);

        // Run 3: deferment gone — second real failure enters degraded
        ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now.AddHours(48));
        Assert.Equal(2, channel.ConsecutiveFailures);
        Assert.True(channel.IsDegraded);
    }

    [Fact]
    public void Full_lifecycle_manual_clear_allows_re_entry()
    {
        var channel = MakeChannel(isDegraded: true, consecutiveFailures: 3);
        channel.DegradedAt = Now.AddDays(-2);

        // User manually clears
        ChannelDegradedStateService.ClearDegradedManually(channel, Now);
        Assert.False(channel.IsDegraded);
        Assert.Equal(0, channel.ConsecutiveFailures);

        // Channel re-enters active pool — but if the problem persists, fails again
        var t1 = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now.AddHours(1));
        Assert.Equal(ChannelDegradedTransition.ConsecutiveFailureIncremented, t1);
        Assert.False(channel.IsDegraded);

        var t2 = ChannelDegradedStateService.RecordAdapterFailure(channel, hasDeferment: false, Now.AddHours(25));
        Assert.Equal(ChannelDegradedTransition.DegradedEntered, t2);
        Assert.True(channel.IsDegraded);
    }
}
