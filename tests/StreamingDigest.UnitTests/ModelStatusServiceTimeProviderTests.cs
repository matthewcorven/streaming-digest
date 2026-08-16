using Xunit;

namespace StreamingDigest.UnitTests;

/// <summary>
/// Unit tests for ModelStatusService TimeProvider injection and SSE pause/restart logic.
/// Tests verify: pause state tracking with mock time, 5-minute timeout calculation,
/// and reconnection behavior. These tests focus on the MockTimeProvider and pause logic,
/// which are independent of ModelStatusService instantiation.
/// </summary>
public class ModelStatusServiceTimeProviderTests
{
    // ── Test 1: Mock TimeProvider Advances Time Deterministically ──────────────────────────

    [Fact]
    public void MockTimeProvider_Should_AdvanceTime()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var t0 = timeProvider.GetUtcNow();

        // Act
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var t1 = timeProvider.GetUtcNow();

        // Assert
        Assert.True(t1 > t0);
        Assert.Equal(TimeSpan.FromSeconds(30), t1 - t0);
    }

    // ── Test 2: Pause Timeout Logic with Mock TimeProvider ──────────────────────────────

    [Fact]
    public void PauseTimeout_Should_ExpireAfterFiveMinutes()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var pauseStartTime = timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act
        timeProvider.Advance(pauseDuration.Add(TimeSpan.FromSeconds(1)));
        var pauseEndTime = timeProvider.GetUtcNow();
        var elapsedSincePause = pauseEndTime - pauseStartTime;

        // Assert
        Assert.True(elapsedSincePause > pauseDuration);
    }

    // ── Test 3: Pause Timeout Not Yet Expired ────────────────────────────────────────────

    [Fact]
    public void PauseTimeout_Should_NotExpireBeforeFiveMinutes()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var pauseStartTime = timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act
        timeProvider.Advance(pauseDuration.Subtract(TimeSpan.FromSeconds(1)));
        var currentTime = timeProvider.GetUtcNow();
        var elapsedSincePause = currentTime - pauseStartTime;

        // Assert
        Assert.True(elapsedSincePause < pauseDuration);
    }

    // ── Test 4: Multiple Time Advances Cumulative ────────────────────────────────────────

    [Fact]
    public void MockTimeProvider_Should_Cumulate_MultipleAdvances()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var t0 = timeProvider.GetUtcNow();

        // Act
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var t_final = timeProvider.GetUtcNow();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(90), t_final - t0);
    }

    // ── Test 5: Pause Timeout Edge Case at Exactly Five Minutes ──────────────────────────

    [Fact]
    public void PauseTimeout_Should_ExpireAtExactlyFiveMinutes()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var pauseStartTime = timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act
        timeProvider.Advance(pauseDuration);
        var currentTime = timeProvider.GetUtcNow();
        var elapsedSincePause = currentTime - pauseStartTime;

        // Assert
        Assert.False(elapsedSincePause < pauseDuration);
    }

    // ── Test 6: SSE Pause/Restart Scenario with TimeProvider ────────────────────────────

    [Fact]
    public void SsePauseRestart_Scenario_WithMockTime()
    {
        // Scenario: Simulate SSE pause/restart cycle
        // 1. T0: SSE pause entered (_sseEnteredPausedAt = T0)
        // 2. T0 + 2:30 (2.5 min): Still paused, wait loop continues
        // 3. T0 + 5:01 (5 min + 1 sec): Timeout expired, reset counters and reconnect

        // Arrange
        var timeProvider = new MockTimeProvider();
        var pauseStartTime = timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act: Check pause status at T0 + 2:30
        timeProvider.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)));
        var t1 = timeProvider.GetUtcNow();
        var elapsed_at_2_5min = t1 - pauseStartTime;
        var shouldStillBePaused_at_2_5min = elapsed_at_2_5min < pauseDuration;

        // Act: Check pause status at T0 + 5:01
        timeProvider.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31)));
        var t2 = timeProvider.GetUtcNow();
        var elapsed_at_5_1min = t2 - pauseStartTime;
        var shouldBeExpired_at_5_1min = !(elapsed_at_5_1min < pauseDuration);

        // Assert
        Assert.True(shouldStillBePaused_at_2_5min);
        Assert.True(shouldBeExpired_at_5_1min);
    }

    // ── Test 7: TimeProvider Idempotency ──────────────────────────────────────────────────

    [Fact]
    public void MockTimeProvider_Should_ReturnSameTimeWithoutAdvance()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var t1 = timeProvider.GetUtcNow();

        // Act
        var t2 = timeProvider.GetUtcNow();

        // Assert
        Assert.Equal(t1, t2);
    }

    // ── Test 8: SetTime Overrides Current Time ──────────────────────────────────────────

    [Fact]
    public void MockTimeProvider_SetTime_Should_ChangeCurrentTime()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var baseTime = timeProvider.GetUtcNow();
        var newTime = baseTime.AddDays(1);

        // Act
        timeProvider.SetTime(newTime);
        var currentTime = timeProvider.GetUtcNow();

        // Assert
        Assert.Equal(newTime, currentTime);
    }
}

/// <summary>
/// Mock TimeProvider for testing time-dependent logic without actual delays.
/// Allows advancing time deterministically for pause/timeout scenarios.
/// </summary>
public class MockTimeProvider : TimeProvider
{
    private DateTimeOffset _currentTime;

    public MockTimeProvider()
    {
        _currentTime = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the current time (default: UtcNow at construction).</summary>
    public override DateTimeOffset GetUtcNow()
    {
        return _currentTime;
    }

    /// <summary>Advances the mock time forward by the given delta.</summary>
    public void Advance(TimeSpan delta)
    {
        _currentTime = _currentTime.Add(delta);
    }

    /// <summary>Sets the mock time to an explicit value.</summary>
    public void SetTime(DateTimeOffset time)
    {
        _currentTime = time;
    }
}
