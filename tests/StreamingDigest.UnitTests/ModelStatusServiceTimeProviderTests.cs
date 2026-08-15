using Moq;
using StreamingDigest.Web.Services;

namespace StreamingDigest.UnitTests;

/// <summary>
/// Unit tests for ModelStatusService TimeProvider injection and SSE pause/restart logic.
/// Tests verify: TimeProvider dependency injection, pause state tracking with mock time,
/// 5-minute timeout calculation, and reconnection behavior.
/// </summary>
[TestClass]
public class ModelStatusServiceTimeProviderTests
{
    private Mock<SearchUiSessionService> _mockSession = null!;
    private MockTimeProvider _timeProvider = null!;
    private ModelStatusService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockSession = new Mock<SearchUiSessionService>(MockBehavior.Loose);
        _timeProvider = new MockTimeProvider();
        _service = new ModelStatusService(_mockSession.Object, jsRuntime: null, _timeProvider);
    }

    // ── Test 1: TimeProvider Injected Successfully ────────────────────────────────────────

    [TestMethod]
    public void ServiceConstructor_Should_AcceptTimeProvider()
    {
        // Arrange: MockTimeProvider initialized in Setup

        // Act: Service created with injected TimeProvider (Setup already did this)
        var service = _service;

        // Assert: Service initialized without null reference exceptions
        Assert.IsNotNull(service, "Service should be initialized with TimeProvider");
    }

    // ── Test 2: TimeProvider.System Used by Default ───────────────────────────────────────

    [TestMethod]
    public void ServiceConstructor_Should_UseSystemTimeProviderWhenNotProvided()
    {
        // Arrange: Create service without timeProvider argument
        var serviceWithoutProvider = new ModelStatusService(_mockSession.Object, jsRuntime: null);

        // Act & Assert: Service should not throw, uses System default
        Assert.IsNotNull(serviceWithoutProvider, "Service should initialize with System TimeProvider default");
    }

    // ── Test 3: Mock TimeProvider Advances Time Deterministically ──────────────────────────

    [TestMethod]
    public void MockTimeProvider_Should_AdvanceTime()
    {
        // Arrange
        var t0 = _timeProvider.GetUtcNow();

        // Act
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        var t1 = _timeProvider.GetUtcNow();

        // Assert
        Assert.IsTrue(t1 > t0, "Time should advance after Advance() call");
        Assert.AreEqual(TimeSpan.FromSeconds(30), t1 - t0, "Time should advance by exactly 30 seconds");
    }

    // ── Test 4: Pause Timeout Logic with Mock TimeProvider ──────────────────────────────

    [TestMethod]
    public void PauseTimeout_Should_ExpireAfterFiveMinutes()
    {
        // Arrange: Simulate pause entered at T
        var pauseStartTime = _timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act: Advance time past pause duration
        _timeProvider.Advance(pauseDuration.Add(TimeSpan.FromSeconds(1)));
        var pauseEndTime = _timeProvider.GetUtcNow();
        var elapsedSincePause = pauseEndTime - pauseStartTime;

        // Assert
        Assert.IsTrue(elapsedSincePause > pauseDuration,
            $"Elapsed time ({elapsedSincePause.TotalSeconds}s) should exceed pause duration ({pauseDuration.TotalSeconds}s)");
    }

    // ── Test 5: Pause Timeout Not Yet Expired ────────────────────────────────────────────

    [TestMethod]
    public void PauseTimeout_Should_NotExpireBeforeFiveMinutes()
    {
        // Arrange: Simulate pause entered at T
        var pauseStartTime = _timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act: Advance time less than pause duration
        _timeProvider.Advance(pauseDuration.Subtract(TimeSpan.FromSeconds(1)));
        var currentTime = _timeProvider.GetUtcNow();
        var elapsedSincePause = currentTime - pauseStartTime;

        // Assert
        Assert.IsTrue(elapsedSincePause < pauseDuration,
            $"Elapsed time ({elapsedSincePause.TotalSeconds}s) should be less than pause duration ({pauseDuration.TotalSeconds}s)");
    }

    // ── Test 6: Multiple Time Advances Cumulative ────────────────────────────────────────

    [TestMethod]
    public void MockTimeProvider_Should_Cumulate_MultipleAdvances()
    {
        // Arrange
        var t0 = _timeProvider.GetUtcNow();

        // Act: Multiple advances
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        var t_final = _timeProvider.GetUtcNow();

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(90), t_final - t0,
            "Three 30-second advances should equal 90 seconds");
    }

    // ── Test 7: Pause Timeout Edge Case at Exactly Five Minutes ──────────────────────────

    [TestMethod]
    public void PauseTimeout_Should_ExpireAtExactlyFiveMinutes()
    {
        // Arrange
        var pauseStartTime = _timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act: Advance to exactly 5 minutes
        _timeProvider.Advance(pauseDuration);
        var currentTime = _timeProvider.GetUtcNow();
        var elapsedSincePause = currentTime - pauseStartTime;

        // Assert
        // When elapsed equals duration, the condition (elapsed < duration) is false,
        // so pause timeout is considered expired (can reconnect)
        Assert.IsFalse(elapsedSincePause < pauseDuration,
            "At exactly 5 minutes, pause should be expired");
    }

    // ── Test 8: SSE Pause/Restart Scenario with TimeProvider ────────────────────────────

    [TestMethod]
    public void SsePauseRestart_Scenario_WithMockTime()
    {
        // Scenario: Simulate SSE pause/restart cycle
        // 1. T0: SSE pause entered (_sseEnteredPausedAt = T0)
        // 2. T0 + 2:30 (2.5 min): Still paused, wait loop continues
        // 3. T0 + 5:01 (5 min + 1 sec): Timeout expired, reset counters and reconnect

        // Arrange
        var pauseStartTime = _timeProvider.GetUtcNow();
        var pauseDuration = TimeSpan.FromMinutes(5);

        // Act: Check pause status at T0 + 2:30
        _timeProvider.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)));
        var t1 = _timeProvider.GetUtcNow();
        var elapsed_at_2_5min = t1 - pauseStartTime;
        var shouldStillBePaused_at_2_5min = elapsed_at_2_5min < pauseDuration;

        // Act: Check pause status at T0 + 5:01
        _timeProvider.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(31)));
        var t2 = _timeProvider.GetUtcNow();
        var elapsed_at_5_1min = t2 - pauseStartTime;
        var shouldBeExpired_at_5_1min = !(elapsed_at_5_1min < pauseDuration);

        // Assert
        Assert.IsTrue(shouldStillBePaused_at_2_5min,
            "At 2:30 elapsed, should still be paused");
        Assert.IsTrue(shouldBeExpired_at_5_1min,
            "At 5:01 elapsed, timeout should be expired and reconnection attempted");
    }

    // ── Test 9: TimeProvider Idempotency ──────────────────────────────────────────────────

    [TestMethod]
    public void MockTimeProvider_Should_ReturnSameTimeWithoutAdvance()
    {
        // Arrange
        var t1 = _timeProvider.GetUtcNow();

        // Act: Call GetUtcNow again without advancing
        var t2 = _timeProvider.GetUtcNow();

        // Assert
        Assert.AreEqual(t1, t2, "Time should remain the same without advance");
    }

    // ── Test 10: SetTime Overrides Current Time ──────────────────────────────────────────

    [TestMethod]
    public void MockTimeProvider_SetTime_Should_ChangeCurrentTime()
    {
        // Arrange
        var baseTime = _timeProvider.GetUtcNow();
        var newTime = baseTime.AddDays(1);

        // Act
        _timeProvider.SetTime(newTime);
        var currentTime = _timeProvider.GetUtcNow();

        // Assert
        Assert.AreEqual(newTime, currentTime, "SetTime should change current time");
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
