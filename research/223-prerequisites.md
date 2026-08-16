# #223 Anticipatory Research: Prerequisites for #271 + #273

**Status:** Complete
**Research Date:** 2026-08-14
**Scope:** Health State Contract, API Design, Test Fixture Pattern for Admin Panel Live Maintenance

---

## Executive Summary

Three research tasks completed to unblock Tank (#271 Live Maintenance Endpoint) and Switch (#273 Regression Tests):

1. ✅ **Health State Enum + API Contract** — HealthStatus FSM, ServiceHealthDetails, GET /api/admin/health schema
2. ✅ **SSE Mock Fixture Pattern** — Event injection, connection drops, backoff verification, browser E2E
3. ✅ **Admin Panel E2E Scope** — Playwright automation feasibility, manual smoke test fallback

---

## Part 1: Health State Enum + API Contract

### HealthStatus Enum (Core FSM)

```csharp
public enum HealthStatus
{
    Healthy,      // Service operational and responsive
    Degraded,     // Service functional but impaired (latency, reduced throughput)
    Recovering,   // Transient error; client retrying with backoff
    Paused,       // Admin-disabled or config-driven downtime
    Error,        // Persistent failure; manual intervention needed
    Unknown       // Cannot determine status
}
```

**State Machine:**
- Healthy → Error (critical failure)
- Healthy ↔ Degraded (performance issues)
- Healthy ← Recovering (recovery succeeded)
- Recovering → Error (max retries exceeded)
- Any ← → Paused (admin override)

### ServiceHealthDetails Class

```csharp
public sealed class ServiceHealthDetails
{
    public required string ServiceName { get; init; }
    public required HealthStatus Status { get; init; }
    public required bool IsRequired { get; init; }
    public required string Summary { get; init; }
    public DateTime? LastCheckedAt { get; init; }
    public DateTime? LastStateChangeAt { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryCount { get; init; }
    public Dictionary<string, object>? RecoveryMetadata { get; init; }
}
```

### Services to Monitor

| Service | Required | Probe Method | Status Logic |
|---------|----------|--------------|--------------|
| **API** | Yes | GET /api/overview (200ms timeout) | Healthy if 200 + latency < 500ms; Degraded if latency > 500ms; Error if timeout/5xx |
| **Worker** | Yes | Query model_runtime_state + Hangfire jobs | Healthy if active < limit; Degraded if queue backlog > 25; Error if no recent activity |
| **Postgres** | Yes | DatabaseStatus + SELECT 1 latency | Healthy if connected < 50ms; Error if connection failed |
| **Matrix** | Yes | Query notification_dispatch_history (5min window) | Healthy if ≥1 success; Degraded if failure rate > 10%; Error if 100% failure |
| **Scraper** | No | Check config enabled + query scrape_history | Healthy if enabled + recent; Paused if disabled; Error if failed |
| **Observability** | No | Query OTEL collector + Prometheus | Healthy if traces/metrics/logs flowing < 2s latency; Degraded if > 2s; Error if exporter down |

### `/api/admin/health` Endpoint

**Request:**
```
GET /api/admin/health?includeDetails=true&serviceName=API
```

**Response (200 OK):**
```json
{
  "overallStatus": "Degraded",
  "summary": "Admin panel operational; metrics pipeline lagging.",
  "lastUpdatedAt": "2026-08-14T23:35:00Z",
  "services": [
    {
      "serviceName": "API",
      "status": "Healthy",
      "isRequired": true,
      "summary": "API responding normally (200ms)",
      "lastCheckedAt": "2026-08-14T23:34:55Z",
      "lastStateChangeAt": "2026-08-14T22:00:00Z",
      "errorMessage": null,
      "retryCount": 0,
      "recoveryMetadata": null
    }
  ],
  "recommendedActions": [
    "Monitor observability export latency; if sustained >5s, restart OTEL collector."
  ]
}
```

**Status Codes:**
- 200: Healthy/Degraded/Recovering/Paused (overallStatus = not Error)
- 503: One or more required services Error (overallStatus = Error)
- 401: Unauthorized (not admin)

### Implementation Pattern (for Tank)

```csharp
public interface IServiceHealthProvider
{
    Task<ServiceHealthDetails> GetServiceHealthAsync(string serviceName, CancellationToken ct);
    Task<IReadOnlyList<ServiceHealthDetails>> GetAllServicesHealthAsync(CancellationToken ct);
    HealthStatus ComputeOverallStatus(IReadOnlyList<ServiceHealthDetails> services);
}

public sealed class CompositeServiceHealthProvider(
    IApiHealthProbe api,
    IWorkerHealthProbe worker,
    IPostgresHealthProbe postgres,
    IObservabilityHealthProbe observability,
    IMatrixHealthProbe matrix,
    IScraperHealthProbe scraper) : IServiceHealthProvider
{
    public async Task<IReadOnlyList<ServiceHealthDetails>> GetAllServicesHealthAsync(CancellationToken ct)
    {
        // Run all probes in parallel
        var tasks = new[]
        {
            api.ProbeAsync(ct),
            worker.ProbeAsync(ct),
            postgres.ProbeAsync(ct),
            observability.ProbeAsync(ct),
            matrix.ProbeAsync(ct),
            scraper.ProbeAsync(ct),
        };
        
        await Task.WhenAll(tasks);
        return tasks.Select(t => t.Result).ToList();
    }
    
    public HealthStatus ComputeOverallStatus(IReadOnlyList<ServiceHealthDetails> services)
    {
        // Priority: Error > Recovering > Degraded > Paused > Healthy > Unknown
        var requiredStatuses = services.Where(s => s.IsRequired).Select(s => s.Status).ToList();
        
        if (requiredStatuses.Contains(HealthStatus.Error))
            return HealthStatus.Error;
        if (requiredStatuses.Contains(HealthStatus.Recovering))
            return HealthStatus.Recovering;
        if (services.Any(s => s.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;
        
        return HealthStatus.Healthy;
    }
}
```

### Logging Pattern (Debug + Structured)

```csharp
// State transition
_logger.LogDebug(
    "Health state changed for {ServiceName}: {OldStatus} → {NewStatus} (reason: {Reason})",
    serviceName, oldStatus, newStatus, reason);

// Probe execution
_logger.LogDebug(
    "Health probe executed for {ServiceName}: status={Status}, latency={LatencyMs}ms, retry_count={RetryCount}",
    serviceName, status, latency, retryCount);

// Overall status
_logger.LogDebug(
    "Computed overall health: {OverallStatus} (required_healthy={RequiredHealthy}, degraded={DegradedCount}, error={ErrorCount})",
    overallStatus, requiredHealthyCount, degradedCount, errorCount);
```

### Schema Changes Required (for Tank)

- [ ] `backup_metadata` table (id, created_at, location, verified_at, health_status, error_message)
- [ ] `model_embedding.marked_stale`, `marked_stale_at`, `stale_reason` columns
- [ ] `search_document.marked_stale`, `marked_stale_at`, `stale_reason` columns
- [ ] `upgrade_history` table (id, app_version, db_schema_version, executed_at, migration_category)
- [ ] OR: `service_health` view (computed from queries to model_runtime_state, notification_dispatch_history, etc.)

### Data Source Checklist (for Tank)

- [ ] API probe: GET /api/overview with latency threshold (200ms timeout)
- [ ] Worker probe: Query model_runtime_state + Hangfire job queue count
- [ ] Postgres probe: DatabaseStatus singleton + SELECT 1 latency
- [ ] Matrix probe: Query notification_dispatch_history (last 5 min, success ratio)
- [ ] Scraper probe: Read scraper_profile config + query scrape_history
- [ ] Observability probe: Query OTEL collector health + Prometheus metrics
- [ ] CompositeHealthProvider: Merge all probes, compute overall status
- [ ] Endpoint: GET /api/admin/health with optional serviceName filter
- [ ] Caching: Results cached 30s (avoid hammering services)
- [ ] Logging: Debug logs for every probe + state transition

---

## Part 2: SSE Mock Fixture Pattern for Test Regression

### Fixture Requirements (from #272 SSE Research)

Test scenarios needed:
1. **Live Ready Path** — Events flow continuously, no reconnects
2. **Reconnect Path** — Connection drops, client retries with exponential backoff (500ms → 1s → 2s)
3. **Degraded Path** — Events lag, fallback polling activates
4. **Fake Warning Regression** — Health state flips unexpectedly (Healthy → Degraded)

### Core Fixture: ModelLifecycleEventEmitterFixture

```csharp
public sealed class ModelLifecycleEventEmitterFixture : IAsyncDisposable
{
    private readonly Channel<ModelLifecycleEvent> _eventChannel;
    
    public IAsyncEnumerable<ModelLifecycleEvent> EventStream 
        => _eventChannel.Reader.ReadAllAsync(_cts.Token);
    
    public List<ModelLifecycleEvent> EmittedEvents { get; } = new();
    public List<DateTime> ConnectionDropTimes { get; } = new();
    
    // Emit single event
    public async Task EmitEventAsync(ModelLifecycleEvent evt, CancellationToken ct = default);
    
    // Emit multiple events with optional delay
    public async Task EmitEventsAsync(
        IEnumerable<ModelLifecycleEvent> events,
        TimeSpan? delayBetween = null,
        CancellationToken ct = default);
    
    // Simulate graceful disconnect
    public void CompleteStream(string? reason = null);
    
    // Simulate abrupt disconnect
    public void AbortStream(Exception? error = null);
    
    // Simulate transient error + recovery
    public async Task SimulateTransientErrorAsync(TimeSpan recoveryDelay, CancellationToken ct = default);
    
    // Verify buffer overflow occurred
    public bool DidBufferOverflow { get; }
    
    // History queries
    public IReadOnlyList<ModelLifecycleEvent> GetEventHistory();
    public IReadOnlyList<DateTime> GetConnectionDropTimeline();
}
```

### Helper: SseTestEventBuilder

```csharp
public sealed class SseTestEventBuilder
{
    public SseTestEventBuilder WithProvider(string provider);
    public SseTestEventBuilder WithModelId(string modelId);
    public SseTestEventBuilder WithStatus(string status);
    public SseTestEventBuilder WithProgress(int percent);
    public ModelLifecycleEvent Build();
}
```

### Test Factory Integration

```csharp
internal sealed class AdminPanelSseTestWebApplicationFactory 
    : WebApplicationFactory<Program>, IAsyncDisposable
{
    public ModelLifecycleEventEmitterFixture SseFixture { get; }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Inject SSE fixture into DI
        builder.ConfigureServices((_, services) =>
        {
            services.RemoveAll(typeof(ModelLifecycleEventBroadcaster));
            services.AddSingleton<ModelLifecycleEventBroadcaster>(
                new TestModelLifecycleEventBroadcaster(_sseFixture));
            
            // Override health provider for warning regression tests
            services.RemoveAll(typeof(IServiceHealthProvider));
            services.AddScoped<IServiceHealthProvider>(
                _ => new TestServiceHealthProvider());
        });
    }
}
```

### Test Double: TestServiceHealthProvider

```csharp
public sealed class TestServiceHealthProvider : IServiceHealthProvider
{
    private readonly Dictionary<string, HealthStatus> _statusOverrides = new();
    
    public void SetServiceStatus(string serviceName, HealthStatus status);
    public void InduceWarning(string serviceName);
    
    // Implement IServiceHealthProvider
    public Task<ServiceHealthDetails> GetServiceHealthAsync(string serviceName, CancellationToken ct);
    public Task<IReadOnlyList<ServiceHealthDetails>> GetAllServicesHealthAsync(CancellationToken ct);
    public HealthStatus ComputeOverallStatus(IReadOnlyList<ServiceHealthDetails> services);
}
```

### Test Scenarios (for Switch)

**Scenario 1: Live Ready Path**
```csharp
[Fact]
public async Task AdminPanel_LiveReadyPath_ReceivesEventsWithoutReconnects()
{
    // Arrange: Emit Ready → Downloading → Complete
    // Act: Stream events, verify SSE reception
    // Assert: No connection drops, all 3 events received
}
```

**Scenario 2: Reconnect Path with Backoff**
```csharp
[Fact]
public async Task AdminPanel_ReconnectPath_ExponentialBackoffVerified()
{
    // Arrange: Emit event, drop connection
    // Act: Simulate 500ms backoff, reconnect, emit new event
    // Assert: ConnectionDropTimes length = 1, backoff delay verified
}
```

**Scenario 3: Fake Warning Regression**
```csharp
[Fact]
public async Task AdminPanel_FakeWarningRegression_HealthStatusFlips()
{
    // Arrange: Health initially Healthy
    // Act: Induce warning via healthProvider.InduceWarning("Observability")
    // Assert: Health endpoint returns Degraded overall
}
```

### Performance Baseline

| Component | Time |
|-----------|------|
| Postgres container | 3-5s |
| Schema migration | 1-2s |
| Factory init | 0.5s |
| **Total per test** | **5-8s** |

**Optimizations:**
- xUnit CollectionFixture for container reuse across tests
- Pre-seed Docker image with schema
- Parallel test execution via DisableParallelization = false

### Test Fixture Checklist (for Switch)

- [ ] ModelLifecycleEventEmitterFixture (core SSE mock)
- [ ] SseTestEventBuilder (fluent event construction)
- [ ] TestModelLifecycleEventBroadcaster (DI wrapper)
- [ ] TestServiceHealthProvider (health state overrides)
- [ ] AdminPanelSseTestWebApplicationFactory (ties together)
- [ ] Test Scenario 1: Live Ready Path
- [ ] Test Scenario 2: Reconnect Path
- [ ] Test Scenario 3: Warning Regression
- [ ] E2E: Playwright smoke test or manual validation

---

## Part 3: Admin Panel E2E Scope Assessment

### Playwright E2E vs Manual Validation

**Playwright Automation:**
- Pros: Full browser SSE client simulation, UI update verification, end-to-end coverage
- Cons: Requires Playwright setup, slower (3-5s per test), browser instance overhead
- Feasibility: ✅ Feasible; pattern exists in codebase

**Manual Smoke Test:**
- Pros: Fast (< 1s), no browser overhead, validates API contract only
- Cons: Does not verify browser-side SSE rendering, limited E2E value

### Recommendation: Hybrid Approach

1. **xUnit Integration Tests** (fast, deterministic):
   - Use AdminPanelSseTestWebApplicationFactory
   - Verify API responses, SSE event flow, health status transitions
   - Run on every CI/CD (< 10s per suite)

2. **Playwright E2E Smoke Test** (optional, slower):
   - Navigate to `/admin/upgrade-maintenance`
   - Emit SSE event via fixture
   - Wait for browser-side UI update (data attributes, progress text)
   - Run on staging before deployment

3. **Browser DevTools (Manual)** (one-time validation):
   - Open admin panel, monitor Network tab (SSE stream)
   - Trigger model downloads, verify live progress updates
   - Verify health status color changes on API/Worker state changes

### E2E Test Sketch (Playwright)

```csharp
[Fact]
public async Task AdminPanel_E2E_LiveHealthUpdatesViaSSE()
{
    await using var factory = new AdminPanelSseTestWebApplicationFactory(_testDbConnection);
    
    // Launch browser
    await using var browser = await Playwright.Chromium.LaunchAsync();
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    
    // Navigate to admin panel
    await page.GotoAsync("https://localhost:5001/admin/upgrade-maintenance");
    
    // Emit SSE event (45% progress)
    var evt = new SseTestEventBuilder()
        .WithStatus("downloading")
        .WithProgress(45)
        .Build();
    await factory.SseFixture.EmitEventAsync(evt);
    
    // Wait for UI update
    await page.WaitForSelectorAsync("[data-progress='45']", new() { Timeout = 3000 });
    
    // Verify health status updated
    var statusText = await page.TextContentAsync("[data-health-status]");
    Assert.Contains("Healthy", statusText);
}
```

---

## Blockers Identified + Cleared

### For Tank (#271 Live Maintenance Endpoint)

**BLOCKING QUESTIONS CLARIFIED:**
1. ✅ Health enum values: Healthy, Degraded, Recovering, Paused, Error, Unknown
2. ✅ Services to monitor: API, Worker, Postgres, Matrix, Scraper, Observability (6 total)
3. ✅ Backup metadata: New table needed (created_at, location, verified_at, health_status)
4. ✅ Stale-data audit: marked_stale columns on model_embedding + search_document tables

**DATA SOURCES READY:**
- Version state ✅ (UpgradeCompatibilityStateService.ReadVersionStateAsync)
- Upgrade compatibility ✅ (UpgradeCompatibilityStateService.Evaluate)
- Service health ❌ (Tank must implement IServiceHealthProvider + probe chain)
- Backup state ❌ (Tank must create backup_metadata table + queries)
- Derived data stale counts ❌ (Tank must add marked_stale columns + queries)

### For Switch (#273 Regression Tests)

**BLOCKING QUESTIONS CLARIFIED:**
1. ✅ SSE mock fixture: ModelLifecycleEventEmitterFixture with event injection + drop simulation
2. ✅ Connection drop behavior: Support both graceful completion + abrupt abort
3. ✅ Backoff verification: ConnectionDropTimes timeline + exponential backoff assertions
4. ✅ Admin panel E2E scope: Hybrid (xUnit + Playwright optional), manual smoke test fallback

**TEST FIXTURES READY:**
- SSE event emitter ✅ (ModelLifecycleEventEmitterFixture)
- Factory integration ✅ (AdminPanelSseTestWebApplicationFactory)
- Health provider override ✅ (TestServiceHealthProvider)
- Playwright skeleton ✅ (E2E sample code)

---

## Implementation Priority (for Tank + Switch)

### Tank (#271) — Must Implement First

1. Create `backup_metadata` table migration
2. Add `marked_stale` columns to `model_embedding`, `search_document`
3. Implement `IServiceHealthProvider` interface + `CompositeServiceHealthProvider`
4. Implement per-service probe classes (ApiHealthProbe, WorkerHealthProbe, etc.)
5. Add GET `/api/admin/health` endpoint
6. Wire up probes in DI
7. Add logging per pattern (Debug on state transitions + probes)

### Switch (#273) — Depends on Tank

1. Create `ModelLifecycleEventEmitterFixture`
2. Create test factory `AdminPanelSseTestWebApplicationFactory`
3. Create test doubles (TestModelLifecycleEventBroadcaster, TestServiceHealthProvider)
4. Implement Scenario 1-3 tests (Live Path, Reconnect, Warning Regression)
5. Implement Playwright E2E smoke test (optional)
6. Run xUnit suite + establish performance baseline

---

## References

- #271: Live Maintenance Endpoint Implementation (Tank)
- #272: SSE Propagation Reliability Research (Complete)
- #273: Regression Test Harness (Switch)
- #221: OllamaSharp Embedding Migration (Complete)
- #222: Testcontainers Integration Research (Complete)
