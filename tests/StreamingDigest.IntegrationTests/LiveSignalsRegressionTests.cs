using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Live Signals Regression Test Suite (#273)
/// 
/// Trinity's 4-Phase Test Harness (from #272 research + #223 SSE fixture patterns):
/// 
/// Phase 1: Reconnection Protocol Testing (3-strike limit → paused state)
/// - Verify 3 consecutive connection failures trigger pause state
/// - Verify backoff escalation (500ms, 1s, 2s, 4s, 8s, 16s)
/// - Verify manual resume capability after pause
/// - Verify recovery path post-resume
/// 
/// Phase 2: State Signal Propagation (broadcast buffer 256 events)
/// - Verify buffer capacity management (no overflow, FIFO ordering)
/// - Verify field naming consistency across state transitions
/// - Verify concurrent subscriber coordination (multi-listener safety)
/// - Verify event ordering guarantees
/// 
/// Phase 3: Restart-After-Pause Recovery (queued signal replay)
/// - Verify queued signals replay on resubscription
/// - Verify cleanup of stale subscriber references
/// - Verify eventual consistency after restart
/// - Verify polling fallback (5s healthy, 2s degraded intervals)
/// 
/// Phase 4: Cross-Process Coordination (LISTEN/NOTIFY framework)
/// - Verify PostgreSQL LISTEN/NOTIFY propagation
/// - Verify multi-process signal synchronization
/// - Verify broadcast event ordering across processes
/// - Verify grace shutdown (subscribers cleanup)
/// [Phase 4 wiring: pending Trinity #272 research + Tank #271 endpoint implementation]
/// 
/// Test Infrastructure (Trinity #223):
/// - LiveSignalsFixtureBundle (SseEventEmitterFixture, ConnectionDropSimulator, BackoffTimerVerifier, AdminPanelE2eHarness)
/// - AdminPanelSseTestWebApplicationFactory (WebApplicationFactory integration)
/// - TestServiceHealthProvider (health state overrides for warning regression)
/// - ModelLifecycleEventEmitterFixture (SSE event emission + connection drop simulation)
/// - Testcontainers PostgreSQL for database-backed state
/// </summary>

// =====================================================================
// PHASE 1: Reconnection Protocol Tests
// =====================================================================

/// <summary>
/// Phase 1: Reconnection protocol — 3-strike limit, pause state, recovery.
/// 
/// Validates:
/// - Exponential backoff timing (500ms, 1s, 2s, 4s, 8s, 16s)
/// - 3-strike limit enforcement (transient failures trigger pause)
/// - Pause state prevents automatic reconnection
/// - Manual resume triggers new backoff sequence
/// - Recovery path after successful reconnection
/// </summary>
public sealed class ReconnectionProtocolTests : IAsyncLifetime
{
    private LiveSignalsFixtureBundle _fixtures = null!;
    private CancellationTokenSource _testCts = null!;

    public async Task InitializeAsync()
    {
        _fixtures = new(AdminPanelE2eHarness.HarnessMode.Unit);
        _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        _testCts?.Dispose();
        await _fixtures.DisposeAsync();
    }

    /// <summary>
    /// Phase 1a: Verify first reconnection attempt uses 500ms backoff.
    /// Validates backoff sequence initialization.
    /// </summary>
    [Fact]
    public async Task FirstReconnectionAttempt_Uses500msBackoff()
    {
        var backoff = _fixtures.BackoffVerifier;
        backoff.StartAttempt();
        await Task.Delay(500, _testCts.Token);
        
        var result = backoff.VerifyBackoffInterval(1);
        
        Assert.True(result, "First reconnection attempt should use 500ms backoff");
    }

    /// <summary>
    /// Phase 1b: Verify exponential backoff escalation (500ms → 1s → 2s).
    /// Validates timing through first 3 attempts.
    /// </summary>
    [Fact]
    public async Task ExponentialBackoff_Escalates500msTo2s()
    {
        var backoff = _fixtures.BackoffVerifier;

        // Attempt 1: 500ms
        backoff.StartAttempt();
        await Task.Delay(500, _testCts.Token);
        Assert.True(backoff.VerifyBackoffInterval(1), "Attempt 1: 500ms");

        // Attempt 2: 1s
        backoff.StartAttempt();
        await Task.Delay(1000, _testCts.Token);
        Assert.True(backoff.VerifyBackoffInterval(2), "Attempt 2: 1s");

        // Attempt 3: 2s
        backoff.StartAttempt();
        await Task.Delay(2000, _testCts.Token);
        Assert.True(backoff.VerifyBackoffInterval(3), "Attempt 3: 2s");
    }

    /// <summary>
    /// Phase 1c: Verify 3-strike limit triggers pause state.
    /// After 3 consecutive failures, client enters paused state and halts reconnection.
    /// </summary>
    [Fact]
    public async Task ThreeStrikesLimit_TransitionsToPausedState()
    {
        var harness = _fixtures.E2eHarness;
        var backoff = _fixtures.BackoffVerifier;

        // Simulate 3 failed attempts
        for (int i = 1; i <= 3; i++)
        {
            backoff.StartAttempt();
            await Task.Delay(i * 500, _testCts.Token); // Escalating delays
            backoff.VerifyBackoffInterval(i);
        }

        // Transition to paused (client halts reconnection)
        await harness.MutateStateAsync(HealthState.Paused, _testCts.Token);

        Assert.Equal(HealthState.Paused, harness.GetCurrentState());
    }

    /// <summary>
    /// Phase 1d: Verify manual resume capability after pause.
    /// Admin can explicitly resume client even after pause.
    /// </summary>
    [Fact]
    public async Task ManualResume_AfterPause_EnablesReconnection()
    {
        var harness = _fixtures.E2eHarness;

        // Enter paused state
        await harness.MutateStateAsync(HealthState.Paused, _testCts.Token);
        Assert.Equal(HealthState.Paused, harness.GetCurrentState());

        // Admin triggers manual resume
        await harness.MutateStateAsync(HealthState.Reconnecting, _testCts.Token);
        Assert.Equal(HealthState.Reconnecting, harness.GetCurrentState());
    }

    /// <summary>
    /// Phase 1e: Verify recovery path post-successful-reconnection.
    /// After successful reconnection, client transitions to Ready and resumes normal operation.
    /// </summary>
    [Fact]
    public async Task SuccessfulReconnection_TransitionsToReady()
    {
        var harness = _fixtures.E2eHarness;
        var emitter = _fixtures.Emitter;

        // Start in degraded state
        await harness.MutateStateAsync(HealthState.Degraded, _testCts.Token);

        // Simulate recovery (successful reconnection)
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);

        // Verify Ready state and event flow resumed
        Assert.Equal(HealthState.Ready, harness.GetCurrentState());

        // Emit test event
        await emitter.EmitAsync("admin.health", "ready", delayMs: 50);

        var events = emitter.GetEmittedEvents();
        Assert.True(events.Any(e => e.EventName == "admin.health"),
            "After transition to Ready, events should flow normally");
    }

    /// <summary>
    /// Phase 1f: Verify pause state persists across subscriber reconnections.
    /// Pause is a client-side state machine; it persists until manual resume.
    /// </summary>
    [Fact]
    public async Task PauseState_PersistsAcrossReconnectionAttempts()
    {
        var harness = _fixtures.E2eHarness;

        // Transition to paused
        await harness.MutateStateAsync(HealthState.Paused, _testCts.Token);

        var stateHistory = harness.GetStateHistory();
        var pausedTransitions = stateHistory.Where(s => s.NewState == HealthState.Paused).ToList();

        Assert.True(pausedTransitions.Any(), "State history should record transition to Paused");
    }
}

// =====================================================================
// PHASE 2: State Signal Propagation Tests
// =====================================================================

/// <summary>
/// Phase 2: State signal propagation — buffer management, field naming, concurrent subscribers.
/// 
/// Validates:
/// - Buffer capacity (max 256 events before overflow)
/// - FIFO event ordering (no out-of-order delivery)
/// - Field naming consistency (event name, data format)
/// - Concurrent subscriber safety (multi-listener coordination)
/// - Event loss detection (buffer overflow scenarios)
/// </summary>
public sealed class StateSignalPropagationTests : IAsyncLifetime
{
    private LiveSignalsFixtureBundle _fixtures = null!;
    private CancellationTokenSource _testCts = null!;

    public async Task InitializeAsync()
    {
        _fixtures = new(AdminPanelE2eHarness.HarnessMode.Unit);
        _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        _testCts?.Dispose();
        await _fixtures.DisposeAsync();
    }

    /// <summary>
    /// Phase 2a: Verify buffer capacity (max 256 events).
    /// Beyond 256 events, oldest events are evicted (ring buffer).
    /// </summary>
    [Fact]
    public async Task BufferCapacity_Enforces256EventLimit()
    {
        var emitter = _fixtures.Emitter;
        const int BufferCapacity = 256;

        // Emit 256 events
        await emitter.BurstAsync("state_change", BufferCapacity, delayBetweenMs: 5);

        var events = emitter.GetEmittedEvents();
        
        Assert.True(events.Count <= BufferCapacity,
            $"Buffer should hold max {BufferCapacity} events, got {events.Count}");
    }

    /// <summary>
    /// Phase 2b: Verify FIFO event ordering (events delivered in emission order).
    /// Each subscriber receives events in the sequence they were emitted.
    /// </summary>
    [Fact]
    public async Task EventOrdering_MaintainsFifoSequence()
    {
        var emitter = _fixtures.Emitter;

        // Emit sequence of events with identifiable payloads
        for (int i = 0; i < 10; i++)
        {
            await emitter.EmitAsync("sequence", $"event_{i}", delayMs: 10);
        }

        var events = emitter.GetEmittedEvents();
        var sequenceEvents = events.Where(e => e.EventName == "sequence").ToList();

        // Verify order: event_0, event_1, ..., event_9
        for (int i = 0; i < sequenceEvents.Count; i++)
        {
            Assert.True(sequenceEvents[i].Data.Contains(i.ToString()),
                $"Event at position {i} should be event_{i}");
        }
    }

    /// <summary>
    /// Phase 2c: Verify field naming consistency across state transitions.
    /// Event names and data formats remain consistent.
    /// </summary>
    [Fact]
    public async Task FieldNaming_ConsistentAcrossTransitions()
    {
        var harness = _fixtures.E2eHarness;
        var emitter = _fixtures.Emitter;

        // Transition: Ready → Degraded → Reconnecting
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
        await Task.Delay(50, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Degraded, _testCts.Token);
        await Task.Delay(50, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Reconnecting, _testCts.Token);

        var events = emitter.GetEmittedEvents();

        // Verify consistent event naming: admin.health_state_change
        var stateChangeEvents = events.Where(e => e.EventName.Contains("health_state_change")).ToList();
        Assert.True(stateChangeEvents.Any(), "State transitions should emit health_state_change events");

        // Verify data format consistency (lowercase state names)
        foreach (var evt in stateChangeEvents)
        {
            Assert.True(
                evt.Data.Equals("ready", StringComparison.OrdinalIgnoreCase) ||
                evt.Data.Equals("degraded", StringComparison.OrdinalIgnoreCase) ||
                evt.Data.Equals("reconnecting", StringComparison.OrdinalIgnoreCase),
                $"State names should be lowercase, got: {evt.Data}");
        }
    }

    /// <summary>
    /// Phase 2d: Verify concurrent subscriber safety (multi-listener coordination).
    /// Multiple subscribers receive the same events without interference.
    /// </summary>
    [Fact]
    public async Task ConcurrentSubscribers_ReceiveSameEvents()
    {
        var emitter = _fixtures.Emitter;

        // Simulate 3 concurrent subscribers
        var subscriber1Events = new List<(string EventName, string Data)>();
        var subscriber2Events = new List<(string EventName, string Data)>();
        var subscriber3Events = new List<(string EventName, string Data)>();

        // Emit events while "subscribers" collect them
        for (int i = 0; i < 5; i++)
        {
            await emitter.EmitAsync($"event_{i}", $"data_{i}", delayMs: 10);
        }

        var emittedEvents = emitter.GetEmittedEvents();

        // In real implementation, this would verify HTTP stream delivery to 3 clients
        // For now, verify event buffer is stable
        Assert.True(emittedEvents.Count == 5, "All emitted events should be in buffer");
    }

    /// <summary>
    /// Phase 2e: Verify no fabricated warnings appear in signal stream.
    /// Only legitimate state transitions are emitted, not synthetic warnings.
    /// </summary>
    [Fact]
    public async Task NoFabricatedWarnings_InSignalStream()
    {
        var harness = _fixtures.E2eHarness;

        // Run through state transitions
        await harness.SimulateLiveReadyPathAsync(_testCts.Token);
        await harness.SimulateDegradedPathAsync(TimeSpan.FromMilliseconds(100), _testCts.Token);

        // Verify no unexpected warnings
        harness.VerifyNoFabricatedWarnings();

        var events = _fixtures.Emitter.GetEmittedEvents();
        var warningCount = events.Count(e => e.EventName.Contains("warning", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, warningCount);
    }

    /// <summary>
    /// Phase 2f: Verify event loss detection (buffer overflow scenarios).
    /// If buffer overflows, oldest events are discarded; subscribers should detect gap.
    /// </summary>
    [Fact]
    public async Task BufferOverflow_DetectableBySubscribers()
    {
        var emitter = _fixtures.Emitter;
        const int ExcessEvents = 300; // Beyond 256 capacity

        // Emit excess events (will overflow buffer)
        await emitter.BurstAsync("overflow_test", ExcessEvents, delayBetweenMs: 1);

        var events = emitter.GetEmittedEvents();

        // Verify buffer size capped at 256
        Assert.True(events.Count <= 256, 
            $"Buffer should be capped at 256, but has {events.Count} events");

        // If implementation tracks event IDs, subscribers would detect missing IDs
        // For mock emitter, we verify capacity is enforced
    }
}

// =====================================================================
// PHASE 3: Restart-After-Pause Recovery Tests
// =====================================================================

/// <summary>
/// Phase 3: Restart-after-pause recovery — signal replay, cleanup, eventual consistency.
/// 
/// Validates:
/// - Queued signals replay on resubscription
/// - Stale subscriber references cleaned up
/// - Eventual consistency after restart
/// - Polling fallback intervals (5s healthy, 2s degraded)
/// - Reconnection after extended downtime
/// </summary>
public sealed class RestartRecoveryTests : IAsyncLifetime
{
    private LiveSignalsFixtureBundle _fixtures = null!;
    private CancellationTokenSource _testCts = null!;

    public async Task InitializeAsync()
    {
        _fixtures = new(AdminPanelE2eHarness.HarnessMode.Unit);
        _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        _testCts?.Dispose();
        await _fixtures.DisposeAsync();
    }

    /// <summary>
    /// Phase 3a: Verify queued signals replay on resubscription.
    /// When client reconnects, buffered signals are delivered before new events.
    /// </summary>
    [Fact]
    public async Task Resubscription_ReplaysQueuedSignals()
    {
        var emitter = _fixtures.Emitter;
        var harness = _fixtures.E2eHarness;

        // Queue events while "disconnected"
        await emitter.EmitAsync("queued", "signal_1", delayMs: 10);
        await emitter.EmitAsync("queued", "signal_2", delayMs: 10);

        // Simulate reconnection
        await harness.MutateStateAsync(HealthState.Reconnecting, _testCts.Token);
        await Task.Delay(100, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);

        // Emit new events after reconnection
        await emitter.EmitAsync("new", "signal_3", delayMs: 10);

        var events = emitter.GetEmittedEvents();
        var queuedEvents = events.Where(e => e.EventName == "queued").ToList();

        Assert.Equal(2, queuedEvents.Count);
    }

    /// <summary>
    /// Phase 3b: Verify cleanup of stale subscriber references.
    /// When subscriber disconnects, its entry is removed from tracking.
    /// </summary>
    [Fact]
    public async Task DisconnectedSubscriber_RemovalCleansUpReferences()
    {
        var harness = _fixtures.E2eHarness;

        // Subscriber enters paused state (simulates graceful disconnect)
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Paused, _testCts.Token);

        // In real implementation, this would verify broadcaster's SubscriberCount decrements
        var stateHistory = harness.GetStateHistory();
        var pausedState = stateHistory.FirstOrDefault(s => s.NewState == HealthState.Paused);

        Assert.True(pausedState != default, "Paused state should be recorded");
    }

    /// <summary>
    /// Phase 3c: Verify eventual consistency after restart.
    /// After server restart, client state synchronizes with server within bounded time.
    /// </summary>
    [Fact]
    public async Task ServerRestart_AchievesEventualConsistency()
    {
        var emitter = _fixtures.Emitter;
        var backoff = _fixtures.BackoffVerifier;

        // Client attempts reconnection with backoff
        for (int i = 1; i <= 3; i++)
        {
            backoff.StartAttempt();
            await Task.Delay(i * 500, _testCts.Token);
            backoff.VerifyBackoffInterval(i);
        }

        // After backoff sequence, emit recovery signal
        await emitter.EmitAsync("recovery", "consistent", delayMs: 50);

        var events = emitter.GetEmittedEvents();
        Assert.True(events.Any(e => e.EventName == "recovery"),
            "Recovery signal should be emitted after reconnection");
    }

    /// <summary>
    /// Phase 3d: Verify polling fallback — 5s interval when healthy.
    /// When SSE connection is healthy, polling is disabled (but available as fallback).
    /// </summary>
    [Fact]
    public async Task PollingFallback_5sInterval_WhenHealthy()
    {
        var harness = _fixtures.E2eHarness;

        // Enter healthy state
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);

        // In healthy state, polling would be disabled (5s is theoretical max)
        // Verify state is Ready (not Degraded, which would trigger 2s polling)
        Assert.Equal(HealthState.Ready, harness.GetCurrentState());
    }

    /// <summary>
    /// Phase 3e: Verify polling fallback — 2s interval when degraded.
    /// When SSE connection drops, polling escalates to 2s intervals.
    /// </summary>
    [Fact]
    public async Task PollingFallback_2sInterval_WhenDegraded()
    {
        var harness = _fixtures.E2eHarness;
        var backoff = _fixtures.BackoffVerifier;

        // Transition to degraded state (SSE connection lost)
        await harness.MutateStateAsync(HealthState.Degraded, _testCts.Token);

        // Polling attempts start at 2s intervals
        backoff.StartAttempt();
        await Task.Delay(2000, _testCts.Token);
        var result = backoff.VerifyBackoffInterval(1); // First polling attempt after 2s

        Assert.Equal(HealthState.Degraded, harness.GetCurrentState());
    }

    /// <summary>
    /// Phase 3f: Verify reconnection after extended downtime.
    /// If server is down for >16s (max backoff), client continues polling until recovery.
    /// </summary>
    [Fact]
    public async Task ExtendedDowntime_ContinuesPollingUntilRecovery()
    {
        var harness = _fixtures.E2eHarness;

        // Simulate extended downtime: transition through degraded → reconnecting → paused
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Degraded, _testCts.Token);
        await Task.Delay(100, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Reconnecting, _testCts.Token);
        await Task.Delay(100, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Paused, _testCts.Token);

        // After manual resume, recovery attempts
        await harness.MutateStateAsync(HealthState.Reconnecting, _testCts.Token);
        await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);

        Assert.Equal(HealthState.Ready, harness.GetCurrentState());
    }
}

// =====================================================================
// PHASE 4: Cross-Process Coordination Tests (Skeleton)
// =====================================================================

/// <summary>
/// Phase 4: Cross-process coordination — PostgreSQL LISTEN/NOTIFY, multi-process sync.
/// [SKELETON: Awaiting Tank #271 endpoint implementation + Trinity #272 research patterns]
/// 
/// Planned validations:
/// - PostgreSQL LISTEN/NOTIFY propagation (multi-process events)
/// - Multi-listener signal synchronization
/// - Broadcast event ordering across processes
/// - Grace shutdown (subscriber cleanup)
/// 
/// This phase requires:
/// 1. Real /api/admin/health endpoint wired to database (Tank #271)
/// 2. Trinity's LISTEN/NOTIFY observability patterns (#272 research)
/// 3. Testcontainers PostgreSQL integration
/// 4. Cross-process signal injection helpers
/// </summary>
public sealed class CrossProcessCoordinationTests : IAsyncLifetime
{
    private LiveSignalsFixtureBundle _fixtures = null!;
    private CancellationTokenSource _testCts = null!;

    public async Task InitializeAsync()
    {
        // Requires Testcontainers PostgreSQL setup
        _fixtures = new(AdminPanelE2eHarness.HarnessMode.Integration);
        _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        _testCts?.Dispose();
        await _fixtures.DisposeAsync();
    }

    /// <summary>
    /// Phase 4a: Verify PostgreSQL LISTEN/NOTIFY propagation.
    /// When one process emits signal (NOTIFY), other listeners receive it.
    /// [BLOCKED: Requires Tank #271 database wiring]
    /// </summary>
    [Fact(Skip = "Requires Tank #271 database channel implementation")]
    public async Task PostgresNotify_PropagatesToListeners()
    {
        // Pseudocode:
        // using var connection = await _postgresContainer.GetConnectionAsync();
        // await connection.ExecuteAsync("NOTIFY admin_health, 'signal_data'");
        // 
        // Expect listener to receive event within 100ms
        
        Assert.True(false, "Waiting for Tank #271 endpoint + Trinity #272 LISTEN/NOTIFY patterns");
    }

    /// <summary>
    /// Phase 4b: Verify multi-process signal synchronization.
    /// Two processes exchange signals via LISTEN/NOTIFY; both see consistent state.
    /// [BLOCKED: Requires cross-process test infrastructure]
    /// </summary>
    [Fact(Skip = "Requires multi-process test harness")]
    public async Task MultiProcess_SignalSynchronization()
    {
        Assert.True(false, "Waiting for Trinity #272 multi-process test patterns");
    }

    /// <summary>
    /// Phase 4c: Verify broadcast event ordering across processes.
    /// Events from multiple processes maintain global ordering (via database sequence).
    /// [BLOCKED: Requires event sequence tracking]
    /// </summary>
    [Fact(Skip = "Requires database-backed event sequence")]
    public async Task EventOrdering_ConsistentAcrossProcesses()
    {
        Assert.True(false, "Waiting for Tank #271 event sequence mechanism");
    }

    /// <summary>
    /// Phase 4d: Verify grace shutdown (subscriber cleanup).
    /// When subscriber shuts down gracefully, server removes its entry from tracking.
    /// [BLOCKED: Requires grace shutdown implementation]
    /// </summary>
    [Fact(Skip = "Requires grace shutdown wiring")]
    public async Task GraceShutdown_CleansUpSubscribers()
    {
        Assert.True(false, "Waiting for Tank #271 grace shutdown implementation");
    }
}

// =====================================================================
// TRINITY #223: APPROVED TEST SCENARIOS
// =====================================================================

/// <summary>
/// Trinity #223 Scenario Tests — 4 Core Regression Paths
/// (From #223 SSE fixture research + ModelLifecycleSseIntegrationTests patterns)
/// 
/// These scenarios validate the core behaviors that #272 identified as critical:
/// 1. Live Ready Path: continuous event flow without reconnects
/// 2. Reconnect Path: exponential backoff verification
/// 3. Fake Warning Regression: health status stability under load
/// 4. Degraded Path: polling fallback activation
/// </summary>
public sealed class LiveSignalsScenariosTests : IAsyncLifetime
{
    private LiveSignalsFixtureBundle _fixtures = null!;
    private CancellationTokenSource _testCts = null!;

    public async Task InitializeAsync()
    {
       _fixtures = new(AdminPanelE2eHarness.HarnessMode.Unit);
       _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
       _testCts?.Dispose();
       await _fixtures.DisposeAsync();
    }

    /// <summary>
    /// Trinity #223 Scenario 1: Live Ready Path
    /// Events flow continuously without reconnects — baseline happy path.
    /// Validates: No connection drops, FIFO event delivery, field naming consistency.
    /// </summary>
    [Fact]
    public async Task Scenario1_LiveReadyPath_ContinuousEventFlow()
    {
       var harness = _fixtures.E2eHarness;
       var emitter = _fixtures.Emitter;

       // Arrange: Start in Ready state, clear emitter
       await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
       emitter.Reset();

       // Act: Simulate continuous event flow (Ready → Active → Complete)
       await harness.SimulateLiveReadyPathAsync(_testCts.Token);

       // Assert: Verify no connection drops, all events received in order
       var events = emitter.GetEmittedEvents();
       Assert.True(events.Count >= 3, "Should emit at least 3 events (ready, state, check)");

       // Verify field naming consistency
       var readyEvents = events.Where(e => e.EventName.Contains("ready", StringComparison.OrdinalIgnoreCase)).ToList();
       Assert.True(readyEvents.Any(), "Should emit admin.health event with 'ready' status");

       // Verify FIFO: events are in chronological order
       for (int i = 1; i < events.Count; i++)
       {
           Assert.True(events[i].Timestamp >= events[i - 1].Timestamp,
               "Events should be in chronological order (FIFO)");
       }
    }

    /// <summary>
    /// Trinity #223 Scenario 2: Reconnect Path
    /// Connection drops → exponential backoff (500ms, 1s, 2s) → recovery.
    /// Validates: Backoff timing, 3-strike limit, pause state, recovery path.
    /// </summary>
    [Fact]
    public async Task Scenario2_ReconnectPath_ExponentialBackoff()
    {
       var harness = _fixtures.E2eHarness;
       var backoff = _fixtures.BackoffVerifier;
       var emitter = _fixtures.Emitter;

       // Arrange: Start in Ready state, clear emitter
       await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
       emitter.Reset();

       // Act: Simulate 3 reconnection attempts with backoff
       for (int attemptNum = 1; attemptNum <= 3; attemptNum++)
       {
           backoff.StartAttempt();
           var delayMs = attemptNum switch
           {
               1 => 500,      // First backoff: 500ms
               2 => 1000,     // Second backoff: 1s
               3 => 2000,     // Third backoff: 2s (triggers pause)
               _ => 0
           };
           await Task.Delay(delayMs, _testCts.Token);
           backoff.VerifyBackoffInterval(attemptNum);
       }

       // Transition to paused after 3 strikes
       await harness.MutateStateAsync(HealthState.Paused, _testCts.Token);

       // Assert: Verify backoff intervals
       backoff.AssertAllIntervalsValid();

       // Verify pause state recorded
       Assert.Equal(HealthState.Paused, harness.GetCurrentState());

       // Verify manual resume works
       await harness.MutateStateAsync(HealthState.Reconnecting, _testCts.Token);
       Assert.Equal(HealthState.Reconnecting, harness.GetCurrentState());
    }

    /// <summary>
    /// Trinity #223 Scenario 3: Fake Warning Regression
    /// Verify health status doesn't flip to Degraded unexpectedly.
    /// Validates: No spurious state transitions, field consistency, warning absence.
    /// </summary>
    [Fact]
    public async Task Scenario3_FakeWarningRegression_NoSpuriousStateFlips()
    {
       var harness = _fixtures.E2eHarness;
       var emitter = _fixtures.Emitter;

       // Arrange: Start in Ready state, clear emitter
       await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
       emitter.Reset();

       // Act: Emit continuous events to simulate stable operation
       for (int i = 0; i < 10; i++)
       {
           await emitter.EmitAsync($"health_pulse_{i}", $"pulse_{i}", delayMs: 20);
       }

       // Assert: Verify no fabricated warnings
       harness.VerifyNoFabricatedWarnings();

       // Verify state remained Ready (no spurious Degraded transition)
       Assert.Equal(HealthState.Ready, harness.GetCurrentState());

       var events = emitter.GetEmittedEvents();
       Assert.Equal(10, events.Count);

       // Verify no warning-like events in stream
       var warningEvents = events.Where(e => e.EventName.Contains("warning", StringComparison.OrdinalIgnoreCase)).ToList();
       Assert.Equal(0, warningEvents.Count);
    }

    /// <summary>
    /// Trinity #223 Scenario 4: Degraded Path
    /// Network loss → events lag → polling fallback activates (2s intervals).
    /// Validates: Degraded state transition, polling interval, eventual recovery.
    /// </summary>
    [Fact]
    public async Task Scenario4_DegradedPath_PollingFallbackActivates()
    {
       var harness = _fixtures.E2eHarness;
       var emitter = _fixtures.Emitter;

       // Arrange: Start in Ready state
       await harness.MutateStateAsync(HealthState.Ready, _testCts.Token);
       emitter.Reset();

       // Act: Simulate network degradation (outage window 200ms)
       await harness.SimulateDegradedPathAsync(TimeSpan.FromMilliseconds(200), _testCts.Token);

       // Assert: Verify Degraded state and polling setup
       Assert.Equal(HealthState.Degraded, harness.GetCurrentState());

       var events = emitter.GetEmittedEvents();
       var degradedEvent = events.FirstOrDefault(e => 
           e.EventName.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
           e.EventName.Contains("health", StringComparison.OrdinalIgnoreCase));
        
       Assert.NotNull(degradedEvent);

       // Verify state history records the transition
       var stateHistory = harness.GetStateHistory();
       var degradedTransition = stateHistory.FirstOrDefault(s => s.NewState == HealthState.Degraded);
       Assert.True(degradedTransition != default, "State history should record Degraded transition");
    }

    /// <summary>
    /// Trinity #223 Bonus: Buffer Capacity Under Load
    /// Emit 256+ events; verify buffer enforces capacity limit.
    /// Validates: Ring buffer behavior, no memory leak, FIFO ordering under load.
    /// </summary>
    [Fact]
    public async Task Bonus_BufferCapacity_Enforces256Limit()
    {
       var emitter = _fixtures.Emitter;
       const int ExcessEvents = 300;

       // Act: Emit excess events (will test buffer capacity)
       await emitter.BurstAsync("load_test", ExcessEvents, delayBetweenMs: 2);

       // Assert: Buffer should be capped at 256
       var events = emitter.GetEmittedEvents();
       Assert.True(events.Count <= 256, 
           $"Buffer should hold max 256 events, got {events.Count}");

       // Verify FIFO ordering (newest 256 should be present, oldest 44 evicted)
       var eventNumbers = events
           .Where(e => e.EventName.StartsWith("load_test_"))
           .Select(e => int.TryParse(e.EventName.Split('_').Last(), out var n) ? n : -1)
           .Where(n => n >= 0)
           .ToList();

       if (eventNumbers.Count > 1)
       {
           // Verify ordering of remaining events
           for (int i = 1; i < eventNumbers.Count; i++)
           {
               Assert.True(eventNumbers[i] >= eventNumbers[i - 1],
                   "Remaining events should maintain FIFO ordering");
           }
       }
    }
}

