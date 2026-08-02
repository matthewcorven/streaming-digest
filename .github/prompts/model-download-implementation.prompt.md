---
mode: agent
description: Implement real model downloads with durable worker handoff, SSE status, manual verify, and isolated test model volumes.
---

# Implement Real Model Downloads

Work from a fresh branch created from the current `main` branch, not from any in-progress refactor branch.

## Objective

Replace the current placeholder model-download flow with a real end-to-end implementation that:

- accepts an admin request from the settings UI,
- durably hands off the requested download to backend persistence before acknowledging success,
- executes the download in the worker,
- reports live progress and terminal state over SSE,
- supports explicit manual verify after download,
- keeps integration and E2E test model storage isolated from the normal app model volume.

## Scope

Touch only the model-download slice and directly supporting code paths:

- API request handling and persistence for model-download work
- worker pickup and execution
- operation/status projection for SSE and status reads
- settings/onboarding UI feedback for queue, progress, success, and failure
- test infrastructure needed to validate the flow safely

Do not widen into unrelated settings cleanup, unrelated onboarding changes, or broader model-management redesign unless required to complete this slice.

## Existing Anchors

Start from these repo surfaces:

- `src/StreamingDigest.Web/Pages/Settings.razor` already posts to `/api/models/download` and expects a queued response.
- `src/StreamingDigest.Infrastructure/Persistence/ModelDiscoveryService.cs` currently fakes queue/verify results and updates readiness as best effort.
- `docs/api/API_SPEC.md` already defines `/api/models/options`, `/api/models/download`, and `/api/models/verify`.
- `docs/presentation/PRESENTATION.md` already describes download + verify actions in onboarding/settings and uses SSE for live state.

## Required Behavior

### 1. API and durable handoff

- `POST /api/models/download` must validate the requested model against the supported-model catalog.
- The API must persist a durable work item or operation record before returning success.
- The API success response should mean "accepted after durable handoff," not "best effort attempted in memory."
- Return a stable operation identifier and any status URL / status contract needed by the existing UI patterns.
- If persistence fails, return failure and do not claim the download was queued.

### 2. Worker execution

- The worker must claim queued model-download work and execute the actual download/setup command.
- Use the repo's configured runtime conventions for model storage and Ollama interaction rather than inventing a parallel path.
- Record lifecycle transitions such as queued, running, succeeded, failed, and any useful progress detail that can be surfaced to the UI.
- Make the worker safe against duplicate pickup/retry races.
- Failures must be durable and inspectable, not only written to transient logs.

### 3. SSE and status visibility

- Publish operation progress through the app's SSE mechanism so the UI can move beyond "queued" without polling-only behavior.
- The settings/onboarding surfaces should show at least:
  - queued/starting,
  - running/downloading,
  - succeeded,
  - failed with an actionable summary.
- Preserve the repo's architecture rule that live state is pushed by SSE, with fallback reads only where already appropriate.

### 4. Manual verify

- `POST /api/models/verify` must remain an explicit user action.
- Verification should check the configured/provided model against the real runtime state after download.
- A successful download should not silently replace explicit verify semantics.
- Readiness/onboarding state should reflect real verification results, not just queued work.

### 5. UI expectations

- Keep the current admin/settings flow centered on the existing Settings page and readiness patterns.
- After queueing a download, the UI should transition into a live status view for that operation.
- Surface meaningful error text when queueing fails or when the worker later fails the operation.
- Keep the UI implementation consistent with the existing WASM + API + SSE architecture; do not introduce SSR or SignalR.

### 6. Persistence expectations

- Use durable storage already appropriate for this system so queued work survives API/worker restarts.
- Model-download operations should be queryable enough for SSE projection, troubleshooting, and tests.
- If a new table/entity is required, keep it minimal and specific to this slice.

### 7. Test-volume isolation

- Integration and E2E tests that exercise model downloads or verify behavior must use a test-time model volume/location that is distinct from the normal app model volume.
- Tests must never download into, mutate, or rely on the developer's normal runtime model storage.
- Keep the isolation explicit in test setup so accidental cross-contamination is hard to miss.

## Implementation Order

Build in this order:

1. Define or refine the persistence contract for queued model-download operations.
2. Implement API durable handoff for `/api/models/download`.
3. Implement worker claim/execute/update lifecycle.
4. Project operation state through the existing SSE/status surfaces.
5. Update Settings/onboarding UI to consume queued + live progress states.
6. Tighten manual verify behavior against real runtime state.
7. Add/update integration and E2E tests, including isolated test model volumes.
8. Update API or ops docs only where the implementation changed the contract.

## Testing Expectations

Add or update focused coverage for:

- API rejects unsupported model requests.
- API does not acknowledge success unless the work item is durably persisted.
- Worker executes queued downloads and records terminal success/failure.
- SSE/status payloads reflect the operation lifecycle.
- Manual verify succeeds only when the runtime state actually supports the model.
- Integration/E2E download tests run against a dedicated test model volume/location.

Prefer the narrowest tests that prove behavior, then add one end-to-end slice covering queue -> worker -> status -> verify.

## Validation Before You Finish

- Run the targeted unit/integration/E2E tests for the touched slice.
- Validate the settings flow end to end: queue a supported model, observe live status, complete/fail deterministically, then run manual verify.
- Confirm the test path uses a distinct model volume from the normal app volume.
- Confirm no unrelated files or behaviors were changed.

## Deliverable

Open a PR-ready implementation from a fresh branch off `main` with the model-download slice completed, tested, and documented only where the contract changed.