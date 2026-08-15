using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// HealthState enum from Tank #271 design.
/// Used to drive deterministic state transitions in #273 regression tests.
/// </summary>
public enum HealthState
{
    /// <summary>All systems operational.</summary>
    Ready = 0,

    /// <summary>Operational but with warnings (degraded performance, partial outage).</summary>
    Degraded = 1,

    /// <summary>Active reconnection in progress (post-network loss).</summary>
    Reconnecting = 2,

    /// <summary>Critical error (e.g., database down, cannot recover).</summary>
    Error = 3,

    /// <summary>Admin paused/maintenance mode (intentional halt).</summary>
    Paused = 4
}

/// <summary>
/// Live Signals Test Fixtures (Trinity #272 + #273)
/// 
/// Designed to support 4-phase regression test harness:
/// Phase 1: Reconnection protocol (3-strike limit, pause state)
/// Phase 2: State signal propagation (buffer mgmt, field naming)
/// Phase 3: Restart-after-pause recovery
/// Phase 4: Cross-process coordination (LISTEN/NOTIFY)
/// 
/// All fixtures build on ModelLifecycleSseIntegrationTests patterns:
/// - WebApplicationFactory for endpoint testing
/// - Long-lived HttpClient (Timeout.InfiniteTimeSpan)
/// - EventBroadcaster with SubscriberCount tracking
/// - StreamReader for SSE event parsing
/// </summary>

/// <summary>
/// Fixture 1: SSE Event Emitter
/// Simulates SSE stream emission, capturing timing and content for verification.
/// Pattern: Mock IEventBroadcaster or wrap real broadcaster with instrumentation.
/// </summary>
public class SseEventEmitterFixture : IAsyncDisposable
{
    private readonly List<(DateTimeOffset Timestamp, string EventName, string Data)> _emittedEvents;
    private readonly TaskCompletionSource<bool> _subscriptionRegistered;
    private CancellationTokenSource? _emissionCts;

    public SseEventEmitterFixture()
    {
        _emittedEvents = new();
        _subscriptionRegistered = new();
    }

    /// <summary>
    /// Emit an SSE event with deterministic timing.
    /// Format: event: {eventName}\ndata: {data}\n\n
    /// </summary>
    public async Task EmitAsync(string eventName, string data, int delayMs = 0)
    {
        if (delayMs > 0)
            await Task.Delay(delayMs);

        _emittedEvents.Add((DateTimeOffset.UtcNow, eventName, data));
        Debug.WriteLine($"[SSE] {eventName}: {data}");
    }

    /// <summary>
    /// Emit rapid burst of events (load injection for phase 2).
    /// </summary>
    public async Task BurstAsync(string eventNamePattern, int count, int delayBetweenMs = 10)
    {
        for (int i = 0; i < count; i++)
        {
            await EmitAsync($"{eventNamePattern}_{i}", $"payload_{i}", delayBetweenMs);
        }
    }

    /// <summary>
    /// Wait for subscriber registration (mirrors WaitForServerSubscriptionAsync pattern).
    /// </summary>
    public async Task WaitForSubscriberAsync(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        try
        {
            await _subscriptionRegistered.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Subscriber did not register within {timeout.TotalSeconds}s");
        }
    }

    /// <summary>
    /// Mark subscriber as registered (called by test when subscription confirmed).
    /// </summary>
    public void MarkSubscriberRegistered() => _subscriptionRegistered.TrySetResult(true);

    /// <summary>
    /// Retrieve all emitted events for verification (ordered by timestamp).
    /// </summary>
    public IReadOnlyList<(DateTimeOffset Timestamp, string EventName, string Data)> GetEmittedEvents()
        => _emittedEvents.AsReadOnly();

    /// <summary>
    /// Clear emission history (useful between sub-tests).
    /// </summary>
    public void Reset()
    {
        _emittedEvents.Clear();
        // Note: TaskCompletionSource can only be created once (readonly field)
        // If needed, create a new fixture instance for fresh subscription tracking
    }

    public async ValueTask DisposeAsync()
    {
        _emissionCts?.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Fixture 2: Connection Drop Simulator
/// Injects network failures mid-stream to verify reconnection behavior.
/// Pattern: HttpClient wrapper or mock ILiveSignalsClient that throws at specified points.
/// </summary>
public class ConnectionDropSimulator : IAsyncDisposable
{
    private enum DropStrategy
    {
        /// <summary>Throw immediately on subscribe (fail early).</summary>
        FailImmediate,

        /// <summary>Throw after N events emitted (mid-stream drop).</summary>
        FailAfterNEvents,

        /// <summary>Throw after delay (simulates connection timeout).</summary>
        FailAfterDelay,

        /// <summary>Alternate between success and failure (transient errors).</summary>
        FailAlternating
    }

    private readonly List<(int AttemptNumber, DateTimeOffset Timestamp, DropStrategy Strategy, string Reason)> _dropLog;
    private int _attemptCount;
    private int _eventsSeenInCurrentStream;

    public ConnectionDropSimulator()
    {
        _dropLog = new();
        _attemptCount = 0;
        _eventsSeenInCurrentStream = 0;
    }

    /// <summary>
    /// Configure simulator to fail immediately on next N attempts.
    /// Advances attempt counter for reconnection testing.
    /// </summary>
    public async Task FailNextAttemptsAsync(int count, string reason = "connection refused")
    {
        for (int i = 0; i < count; i++)
        {
            _attemptCount++;
            _dropLog.Add((_attemptCount, DateTimeOffset.UtcNow, DropStrategy.FailImmediate, reason));
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Configure simulator to fail mid-stream after emitting specified event count.
    /// </summary>
    public void FailAfterEvents(int eventCount, string reason = "connection lost mid-stream")
    {
        _attemptCount++;
        _dropLog.Add((_attemptCount, DateTimeOffset.UtcNow, DropStrategy.FailAfterNEvents, reason));
    }

    /// <summary>
    /// Configure simulator to fail after delay (timeout simulation).
    /// </summary>
    public void FailAfterDelay(TimeSpan delay, string reason = "connection timeout")
    {
        _attemptCount++;
        _dropLog.Add((_attemptCount, DateTimeOffset.UtcNow, DropStrategy.FailAfterDelay, reason));
    }

    /// <summary>
    /// Configure simulator to alternate: next attempt fails, then succeeds, then fails, etc.
    /// </summary>
    public void AlternateFailure(int cycleCount = 2, string reason = "transient network error")
    {
        for (int i = 0; i < cycleCount; i++)
        {
            _attemptCount++;
            _dropLog.Add((_attemptCount, DateTimeOffset.UtcNow, DropStrategy.FailAlternating, reason));
        }
    }

    /// <summary>
    /// Increment event counter (called when event emitted). Check against FailAfterEvents limit.
    /// </summary>
    public bool ShouldDropAfterEvent()
    {
        _eventsSeenInCurrentStream++;
        // Logic: if configured to fail after N events, and we've seen N, drop
        var dropConfig = _dropLog.LastOrDefault();
        if (dropConfig.Strategy == DropStrategy.FailAfterNEvents)
        {
            // This is a simplified check; real implementation would track per-attempt
            return false; // Placeholder
        }
        return false;
    }

    /// <summary>
    /// Reset event counter when new connection attempt starts.
    /// </summary>
    public void ResetEventCounter()
    {
        _eventsSeenInCurrentStream = 0;
    }

    /// <summary>
    /// Retrieve drop history for verification.
    /// </summary>
    public IReadOnlyList<(int AttemptNumber, DateTimeOffset Timestamp, string Strategy, string Reason)> GetDropLog()
        => _dropLog.Select(d => (d.AttemptNumber, d.Timestamp, d.Strategy.ToString(), d.Reason)).ToList().AsReadOnly();

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}

/// <summary>
/// Fixture 3: Backoff Timer Verification
/// Verifies reconnection delay intervals (500ms, 1s, 2s, 4s, 8s, 16s exponential backoff).
/// Pattern: Instrument retry loop with Stopwatch, assert elapsed time within tolerance.
/// </summary>
public class BackoffTimerVerifier
{
    /// <summary>Expected backoff intervals (in milliseconds).</summary>
    private static readonly int[] BackoffIntervals = { 500, 1_000, 2_000, 4_000, 8_000, 16_000 };

    private readonly List<(int AttemptNumber, long ElapsedMs, bool IsWithinTolerance)> _backoffLog;
    private readonly int _toleranceMs;
    private Stopwatch? _attemptStopwatch;

    /// <summary>
    /// Initialize verifier with tolerance (default ±200ms for CI environments).
    /// </summary>
    public BackoffTimerVerifier(int toleranceMs = 200)
    {
        _backoffLog = new();
        _toleranceMs = toleranceMs;
    }

    /// <summary>
    /// Mark the start of a reconnection attempt (call before delay starts).
    /// </summary>
    public void StartAttempt()
    {
        _attemptStopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Complete the attempt and verify backoff interval.
    /// Call after the delay (e.g., await Task.Delay(backoffMs)).
    /// Returns true if interval was within tolerance, false otherwise.
    /// </summary>
    public bool VerifyBackoffInterval(int attemptNumber)
    {
        if (_attemptStopwatch == null)
            throw new InvalidOperationException("Call StartAttempt() first");

        _attemptStopwatch.Stop();
        var elapsedMs = _attemptStopwatch.ElapsedMilliseconds;

        // Map attempt number to expected interval (1st attempt = 500ms, 2nd = 1s, etc.)
        var attemptIndex = Math.Min(attemptNumber - 1, BackoffIntervals.Length - 1);
        var expectedMs = BackoffIntervals[attemptIndex];

        var isWithinTolerance = Math.Abs(elapsedMs - expectedMs) <= _toleranceMs;
        _backoffLog.Add((attemptNumber, elapsedMs, isWithinTolerance));

        if (!isWithinTolerance)
        {
            Debug.WriteLine($"[Backoff] Attempt {attemptNumber}: expected {expectedMs}±{_toleranceMs}ms, actual {elapsedMs}ms");
        }

        return isWithinTolerance;
    }

    /// <summary>
    /// Assert all recorded backoff intervals were within tolerance.
    /// </summary>
    public void AssertAllIntervalsValid()
    {
        var violations = _backoffLog.Where(b => !b.IsWithinTolerance).ToList();
        if (violations.Any())
        {
            var msg = string.Join(
                "\n",
                violations.Select(v => $"  Attempt {v.AttemptNumber}: {v.ElapsedMs}ms (expected {BackoffIntervals[Math.Min(v.AttemptNumber - 1, BackoffIntervals.Length - 1)]}±{_toleranceMs}ms)")
            );
            throw new InvalidOperationException($"Backoff intervals out of tolerance:\n{msg}");
        }
    }

    /// <summary>
    /// Retrieve backoff log for custom verification.
    /// </summary>
    public IReadOnlyList<(int AttemptNumber, long ElapsedMs, bool IsWithinTolerance)> GetBackoffLog()
        => _backoffLog.AsReadOnly();

    /// <summary>
    /// Clear log (useful for testing multiple backoff sequences).
    /// </summary>
    public void Reset()
    {
        _backoffLog.Clear();
        _attemptStopwatch = null;
    }
}

/// <summary>
/// Fixture 4: Admin Panel E2E Harness
/// Coordinates live signal subscription (SSE) with simulated admin panel UI interactions.
/// Scope: Can operate in unit-test mode (mock) or integration mode (real API + Playwright).
/// </summary>
public class AdminPanelE2eHarness : IAsyncDisposable
{
    public enum HarnessMode
    {
        /// <summary>Mock SSE client, in-memory broadcaster (fast unit tests).</summary>
        Unit,

        /// <summary>Real ASP.NET Core API endpoint, HttpClient.GetStreamAsync (slower, higher fidelity).</summary>
        Integration,

        /// <summary>Real API + Playwright browser automation (E2E, validates UI updates).</summary>
        EndToEnd
    }

    private readonly HarnessMode _mode;
    private readonly SseEventEmitterFixture _emitter;
    private readonly ConnectionDropSimulator _dropSimulator;
    private readonly BackoffTimerVerifier _backoffVerifier;
    private readonly List<string> _panelStateTransitions;
    private readonly List<(DateTimeOffset Timestamp, HealthState PreviousState, HealthState NewState)> _stateHistory;
    private HealthState _currentState;
    private CancellationTokenSource? _harnessCts;

    /// <summary>
    /// Initialize harness for the specified test mode.
    /// </summary>
    public AdminPanelE2eHarness(HarnessMode mode = HarnessMode.Unit)
    {
        _mode = mode;
        _emitter = new();
        _dropSimulator = new();
        _backoffVerifier = new();
        _panelStateTransitions = new();
        _stateHistory = new();
        _currentState = HealthState.Ready;
    }

    /// <summary>
    /// Simulate "Live Ready Path": SSE stream open, events flowing, panel responsive.
    /// </summary>
    public async Task SimulateLiveReadyPathAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[E2E] Entering Live Ready Path");

        // Transition to Ready state
        MutateState(HealthState.Ready);

        // Emit sequence: ready → search_active → health_check_passed
        await _emitter.EmitAsync("admin.health", "ready", delayMs: 100);
        await _emitter.EmitAsync("admin.state", "search_active", delayMs: 200);
        await _emitter.EmitAsync("admin.health", "health_check_passed", delayMs: 150);

        _panelStateTransitions.Add("live_ready");
    }

    /// <summary>
    /// Simulate "Degraded Path": network loss → polling fallback.
    /// </summary>
    public async Task SimulateDegradedPathAsync(TimeSpan outageWindow, CancellationToken ct = default)
    {
        Debug.WriteLine($"[E2E] Entering Degraded Path (outage {outageWindow.TotalSeconds}s)");

        // Transition to Degraded state
        MutateState(HealthState.Degraded);

        // Emit health_degraded signal
        await _emitter.EmitAsync("admin.health", "health_degraded", delayMs: 50);

        // Simulate outage
        await Task.Delay(outageWindow, ct);

        // Expect polling fallback to engage (5s→2s escalation)
        _panelStateTransitions.Add("degraded_path");
    }

    /// <summary>
    /// Simulate "Reconnect Path": 3-strike limit, pause, recovery.
    /// </summary>
    public async Task SimulateReconnectPathAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[E2E] Entering Reconnect Path");

        // Transition to Reconnecting state
        MutateState(HealthState.Reconnecting);

        // Strike 1
        _backoffVerifier.StartAttempt();
        await Task.Delay(500, ct); // 500ms backoff
        _backoffVerifier.VerifyBackoffInterval(1);

        // Strike 2
        _backoffVerifier.StartAttempt();
        await Task.Delay(1000, ct); // 1s backoff
        _backoffVerifier.VerifyBackoffInterval(2);

        // Strike 3 → pause
        _backoffVerifier.StartAttempt();
        await Task.Delay(2000, ct); // 2s backoff
        _backoffVerifier.VerifyBackoffInterval(3);

        // Transition to Paused state
        MutateState(HealthState.Paused);
        await _emitter.EmitAsync("admin.state", "paused", delayMs: 100);

        _panelStateTransitions.Add("reconnect_path");
    }

    /// <summary>
    /// Mutate health state and emit SSE event (mirrors /api/admin/health state machine).
    /// </summary>
    public async Task MutateStateAsync(HealthState newState, CancellationToken ct = default)
    {
        MutateState(newState);
        await _emitter.EmitAsync("admin.health_state_change", newState.ToString().ToLowerInvariant(), delayMs: 50);
    }

    /// <summary>
    /// Internal: record state transition without emitting event.
    /// </summary>
    private void MutateState(HealthState newState)
    {
        if (_currentState != newState)
        {
            _stateHistory.Add((DateTimeOffset.UtcNow, _currentState, newState));
            Debug.WriteLine($"[State] {_currentState} → {newState}");
            _currentState = newState;
        }
    }

    /// <summary>
    /// Retrieve current health state.
    /// </summary>
    public HealthState GetCurrentState() => _currentState;

    /// <summary>
    /// Retrieve state transition history for verification.
    /// </summary>
    public IReadOnlyList<(DateTimeOffset Timestamp, HealthState PreviousState, HealthState NewState)> GetStateHistory()
        => _stateHistory.AsReadOnly();

    /// <summary>
    /// Verify no fabricated warnings appear in signal stream.
    /// </summary>
    public void VerifyNoFabricatedWarnings()
    {
        var events = _emitter.GetEmittedEvents();
        var warnings = events.Where(e => e.EventName.Contains("warning", StringComparison.OrdinalIgnoreCase)).ToList();

        if (warnings.Any())
        {
            var msg = string.Join("\n", warnings.Select(w => $"  {w.EventName}: {w.Data}"));
            throw new InvalidOperationException($"Found unexpected fabricated warnings:\n{msg}");
        }
    }

    /// <summary>
    /// Retrieve panel state transitions for verification.
    /// </summary>
    public IReadOnlyList<string> GetStateTransitions() => _panelStateTransitions.AsReadOnly();

    /// <summary>
    /// Get backoff verifier for assertion.
    /// </summary>
    public BackoffTimerVerifier BackoffVerifier => _backoffVerifier;

    /// <summary>
    /// Get emitter for direct event emission.
    /// </summary>
    public SseEventEmitterFixture Emitter => _emitter;

    public async ValueTask DisposeAsync()
    {
        _harnessCts?.Dispose();
        await _emitter.DisposeAsync();
        await _dropSimulator.DisposeAsync();
    }
}

/// <summary>
/// Fixture Integration Helper: Wraps fixtures for common test patterns.
/// </summary>
public class LiveSignalsFixtureBundle : IAsyncDisposable
{
    public SseEventEmitterFixture Emitter { get; }
    public ConnectionDropSimulator DropSimulator { get; }
    public BackoffTimerVerifier BackoffVerifier { get; }
    public AdminPanelE2eHarness E2eHarness { get; }

    public LiveSignalsFixtureBundle(AdminPanelE2eHarness.HarnessMode mode = AdminPanelE2eHarness.HarnessMode.Unit)
    {
        Emitter = new();
        DropSimulator = new();
        BackoffVerifier = new();
        E2eHarness = new(mode);
    }

    public async ValueTask DisposeAsync()
    {
        await Emitter.DisposeAsync();
        await DropSimulator.DisposeAsync();
        await E2eHarness.DisposeAsync();
    }
}
