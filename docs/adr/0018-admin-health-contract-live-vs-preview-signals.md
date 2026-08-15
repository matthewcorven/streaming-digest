# Admin Health Contract: Live vs. Preview Signals

**Status:** Accepted  
**Deciders:** Morpheus (Lead), Trinity (Frontend)  
**Date:** 2026-08-15

## Context

The admin settings page and health dashboard display system status through multiple endpoints and services. Without a clear contract, operators cannot reliably distinguish:
- **Live signals** (backed by real-time runtime APIs)
- **Preview signals** (expected state, not yet live-verified)
- **Static/demo data** (hardcoded, placeholder)

This ambiguity creates confusion about which status badges represent authoritative health vs. aspirational state.

## Decision

We define four categories of admin signals, each with an authoritative source of truth:

### Live Signals (Authoritative)

**Settings**
- **Source:** Database via `UpgradeCompatibilityStateService.ReadVersionStateAsync()`
- **Data:** App version, DB schema version
- **Endpoint:** `GET /api/admin/health` → `HealthResponse.Settings`
- **Semantics:** Current app and schema versions from system configuration

**Models**
- **Source:** Database via `IModelRuntimeStateRepository.GetAllAsync()`
- **Data:** Per-model runtime state (status, progress, last verified, operation ID)
- **Endpoints:** 
  - `GET /api/models/status` (primary, authoritative)
  - `GET /api/models/events` (SSE, real-time updates)
- **Semantics:** Live model readiness, operation progress, error summaries
- **Note:** Admin health's `Models` section should consume this same repository, not return hardcoded preview

**Observability**
- **Source:** Service health probes via `CompositeServiceHealthProvider.ProbeAllAsync()`
- **Data:** Telemetry pipeline health (traces, metrics, logs collection status)
- **Endpoint:** `GET /api/admin/health` → `HealthResponse.Observability`
- **Semantics:** Real-time telemetry collection and export operational status

**Storage**
- **Source:** Service health probes (PostgreSQL probe)
- **Data:** Database connectivity, latency, pgvector extension status
- **Endpoint:** `GET /api/admin/health` → `HealthResponse.Storage`
- **Semantics:** Database availability and extension health

### Preview Signals (Not Authoritative; Marked Explicitly)

**Backup Readiness**
- **Source:** `UpgradeMaintenanceSnapshotService` (placeholder, static data)
- **Data:** Last backup timestamp, retention policy status
- **Endpoint:** `GET /api/admin/health` → `HealthResponse.BackupReadiness`
- **Marked:** `PreviewMode = true` in response
- **Semantics:** Expected backup state; actual verification pending backup manifest implementation (#271)
- **UI Badge:** Marked with (?) indicator and tooltip: "Live verification pending"

**Upgrade Migration Path**
- **Source:** `UpgradeMaintenanceSnapshotService` (part of maintenance snapshot)
- **Data:** Service compatibility, deployment migration checklist, risk assessment
- **Endpoint:** `GET /api/admin/upgrade/snapshot` (future: #271)
- **Marked:** `PreviewMode = true` in response
- **Semantics:** Expected migration state based on deployment profile; not yet live-synchronized with orchestration layer
- **UI Badge:** Marked with (?) indicator: "Requires manual verification"

## Consequences

### Positive

1. **Clarity for Operators:** Every status badge has a documented source of truth
2. **Preview Signals Explicit:** UI can render preview data with distinct styling (? icon, tooltip)
3. **No Silent Assumptions:** Settings page cannot show "backup healthy" without explicitly marking it as preview
4. **Unblocks #271 & #272:** Defines contract that backend (admin health) and frontend (settings page) must honor
5. **Consolidates Models:** Single source of truth for model status; no duplication

### Negative

1. **Requires UI Changes:** Settings page must show preview indicators for each signal
2. **Backup Validation Deferred:** Actual backup health check deferred to #271; currently static
3. **Upgrade Migration Deferred:** Live deployment orchestration check deferred to #271

## Implementation Roadmap

### #270 (This work: Define Contract)

1. ✅ Create this ADR
2. ✅ Update `HealthResponse.cs` to add `PreviewMode` boolean per section
3. ✅ Update `AdminHealthEndpoints.cs`:
   - Inject `IModelRuntimeStateRepository` into BuildModelsSection()
   - Fetch live model status instead of returning hardcoded preview
   - Set `Models.PreviewMode = false` (now live)
4. ✅ Keep `BackupReadiness.PreviewMode = true` (unchanged until #271)
5. ✅ Update `API_SPEC.md` with live/preview markers for admin health endpoint

### #271 (Scheduled Follow-up: Live Upgrade Snapshot)

1. Implement `IBackupManifestChecker` to read actual backup metadata
2. Replace `UpgradeMaintenanceSnapshotService` hardcoded data with live checks
3. Set `BackupReadiness.PreviewMode = false` (now live)

### #272 (Scheduled Follow-up: Model Status SSE Reliability)

1. Verify model status changes reliably trigger SSE events
2. Add telemetry for SSE connection stability
3. Confirm Settings page receives real-time model updates

## API Contract (After #270)

```json
GET /api/admin/health

{
  "settings": {
    "state": "ready",
    "summary": "Version v0.8.1 ready",
    "details": [],
    "previewMode": false
  },
  "models": {
    "state": "ready",
    "summary": "All models operational",
    "models": [
      {
        "name": "EmbeddingModel",
        "state": "ready",
        "status": "Operational",
        "version": "bge-m3"
      }
    ],
    "activeOperationCount": 0,
    "previewMode": false
  },
  "observability": {
    "state": "ready",
    "summary": "Telemetry collection and export operational",
    "tracesStatus": "Operational",
    "metricsStatus": "Operational",
    "logsStatus": "Operational",
    "details": [],
    "previewMode": false
  },
  "storage": {
    "state": "ready",
    "summary": "Storage systems operational",
    "postgresStatus": "Connected",
    "vectorExtensionStatus": "Ready",
    "details": [],
    "previewMode": false
  },
  "backupReadiness": {
    "state": "ready",
    "summary": "Backup system operational (live verification pending)",
    "lastBackupAt": null,
    "timeSinceLastBackup": "Unknown",
    "retentionStatus": "Not yet verified",
    "details": ["Backup verification is preview state; live check pending implementation."],
    "previewMode": true
  },
  "overallHealth": "ready",
  "lastUpdatedAt": "2026-08-15T20:30:00Z"
}
```

**UI Rendering Rules:**
- Sections with `previewMode: false` → render with ✓ badge (verified, live)
- Sections with `previewMode: true` → render with ? badge + tooltip explaining why preview
- Summary text may include "(live)" or "(preview)" suffix for quick scanning

## References

- ADR-0008: Single active embedding model with transition state
- ADR-0011: Embedding transition, ingestion pause with catch-up
- #270: Define live admin health contract (this issue)
- #271: Replace static maintenance snapshot with live data
- #272: Guarantee model status signals propagate via SSE
