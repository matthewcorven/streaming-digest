# Streaming Digest REST API Spec

Status: MVP API design
Base path: `/api`
Auth: secure cookie session after login
Format: JSON unless otherwise noted

## 1. API principles

- REST API for MVP.
- Mutating endpoints require authentication and CSRF protection.
- All admin/operational endpoints require authentication.
- Errors use consistent RFC 7807-style problem details.
- Long-running operations enqueue application-owned operations and usually Hangfire jobs; callers track `/api/admin/operations/{operationId}` instead of depending on Hangfire internals.
- Re-running work has exactly two verbs (ADR-0002): **Retry** re-executes failed/deferred work only and is idempotent; **Reprocess** re-executes the full pipeline for an already-succeeded entity, bypassing the idempotency guard. "Regenerate" never appears as a user-facing verb.
- Search endpoints return explainable ranking components.
- Top-level MVP search results are video clusters. Use `matchedDocumentTypes` to filter which document types may match inside clusters.
- Detail/edit DTOs for scraped editable fields expose `original`, `override`, and `effective` values.
- Mutation responses include stale search documents, stale cluster embeddings, and queued operations when applicable.

## 2. Common response shapes

### 2.1 Error

```json
{
  "type": "https://streaming-digest/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "errors": {
    "channelUrl": ["Channel URL is required."]
  },
  "traceId": "..."
}
```

### 2.2 Paged result

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "totalCount": 100
}
```

Pagination conventions:

- MVP uses page-based pagination: `page` is 1-based and `pageSize` defaults to `25` unless an endpoint states otherwise.
- Endpoints that support sorting accept `sort` and `direction` query parameters unless the endpoint documents a fixed order.
- `direction` values are `asc` and `desc`.
- Default sort orders:
  - Search: ranking score descending, with deterministic tie-breakers.
  - Ingestion runs, operations, backups, recent searches, and events: newest first.
  - Channels and repositories: name ascending where applicable.
- Cursor pagination is MVP+ if datasets grow beyond page-based comfort.

### 2.3 Accepted operation/job

```json
{
  "status": "accepted",
  "operationId": "uuid",
  "jobId": "hangfire-job-id",
  "ingestionRunId": "uuid",
  "statusUrl": "/api/admin/operations/{operationId}"
}
```

`jobId` and `ingestionRunId` are nullable and present only when relevant.

### 2.4 Immediate mutation

```json
{
  "status": "updated",
  "entityType": "video",
  "entityId": "uuid",
  "resource": {},
  "staleSearchDocumentIds": ["uuid"],
  "staleClusterEmbeddingIds": ["uuid"],
  "queuedOperations": [
    {
      "operationId": "uuid",
      "operationType": "reprocess_embeddings",
      "statusUrl": "/api/admin/operations/{operationId}"
    }
  ]
}
```

`staleSearchDocumentIds` and `staleClusterEmbeddingIds` report entities whose stored content no longer matches current Effective Values (staleness is derived, ADR-0001); the queued reprocess clears them as a downstream consequence.

### 2.5 Batch operation request

Batch endpoints use a common item shape and should be all-or-report rather than all-or-nothing unless explicitly documented. Each item returns its own status/error so safe partial success is visible.

```json
{
  "items": [
    {
      "entityType": "search_document",
      "entityId": "uuid",
      "action": "reprocess"
    }
  ],
  "dryRun": false
}
```

Batch response:

```json
{
  "status": "accepted",
  "operationId": "uuid",
  "acceptedCount": 10,
  "rejectedCount": 1,
  "itemResults": [
    {
      "entityType": "search_document",
      "entityId": "uuid",
      "status": "accepted",
      "error": null
    }
  ],
  "statusUrl": "/api/admin/operations/{operationId}"
}
```

### 2.6 Editable field value

Detail/edit DTOs should use this shape for scraped editable fields:

```json
{
  "original": "Original scraped value",
  "override": "User override or null",
  "effective": "Value used for display/search/embedding"
}
```

List DTOs may flatten effective values for brevity, but detail/edit responses should preserve all three values.

## 3. Setup and auth endpoints

### GET `/api/setup/status`

Returns whether first-time setup is still required.

```json
{
  "setupRequired": true,
  "userCount": 0
}
```

Notes:

- Public endpoint.
- `setupRequired` is `true` only while `public.app_users` has zero rows.

### POST `/api/setup/initialize`

Creates the first local user only while `public.app_users` is empty.

Request:

```json
{
  "username": "admin",
  "password": "setup-passw0rd!"
}
```

Successful response:

```json
{
  "username": "admin"
}
```

Notes:

- Public endpoint.
- Rejects the request once any user already exists.
- Stores only the Argon2id hash; plaintext passwords are never persisted.
- Does not create an authenticated session. The client redirects to `/login` after success.

### POST `/api/auth/login`

Request:

```json
{
  "username": "admin",
  "password": "secret"
}
```

Response:

```json
{
  "username": "admin",
  "mustChangePassword": true
}
```

Notes:

- Rate limited.
- Sets secure HTTP-only auth cookie.
- `mustChangePassword` is expected only for the environment-bootstrap path; users created through `/api/setup/initialize` sign in normally without a synthetic password-change step.

### POST `/api/auth/logout`

Clears session cookie.

### GET `/api/auth/me`

Response:

```json
{
  "username": "admin",
  "mustChangePassword": false
}
```

### GET `/api/auth/csrf`

Returns or refreshes the CSRF token used by mutating endpoints. Exact cookie/header mechanics are implementation-defined but must be documented for the Blazor client.

### POST `/api/auth/change-password`

Request:

```json
{
  "currentPassword": "old",
  "newPassword": "new"
}
```

## 4. Onboarding and readiness endpoints

### GET `/api/onboarding/status`

Returns core setup and full readiness state.

```json
{
  "isCoreSetupComplete": false,
  "isFullyReady": false,
  "steps": [
    {
      "key": "embedding_model_verified",
      "label": "Embedding model",
      "status": "failed",
      "requiredForCoreSetup": true,
      "requiredForFullReadiness": true,
      "lastCheckedAt": "2026-07-17T10:00:00Z",
      "lastSuccessAt": null,
      "errorSummary": "Ollama model bge-m3 not available.",
      "details": {}
    }
  ]
}
```

### POST `/api/onboarding/steps/{stepKey}/verify`

Runs the verification for one onboarding/readiness step and updates `app_readiness_checks`.

Response: immediate mutation or accepted operation, depending on step duration.

### POST `/api/onboarding/complete-core-setup`

Marks core setup complete only when required core checks have succeeded.

## 5. Settings, config, and model endpoints

### GET `/api/settings`

Returns user/admin configurable database-backed settings.

### PUT `/api/settings`

Request example:

```json
{
  "ingestion.defaultMaxAgeDays": 30,
  "ingestion.defaultConcurrency": 1,
  "ingestion.maxSegmentsPerVideo": 60,
  "screenshots.offsetSeconds": 5,
  "search.textWeight": 0.5,
  "search.vectorWeight": 0.5,
  "notifications.matrix.onManualRuns": true,
  "notifications.matrix.onScheduledRuns": true
}
```

Response: immediate mutation.

### GET `/api/config/runtime`

Returns non-secret runtime/deployment config diagnostics and schema versions. Secrets are never returned.

### GET `/api/config/schema`

Returns JSON Schema metadata for runtime/deployment config files.

### POST `/api/config/validate`

Validates candidate non-secret config JSON and returns exact JSON-path errors.

### GET `/api/models/options`

Returns the hard-coded catalog of supported embedding, LLM, and audio models, including CLI download commands and mount-path hints for Ollama-managed models. Requires authentication.

Response:

```json
{
  "models": [
    {
      "id": "bge-m3",
      "family": "embedding",
      "provider": "ollama",
      "runtimeRole": "embedding",
      "downloadable": true,
      "status": "available",
      "label": "BAAI bge-m3",
      "installCommand": "ollama pull bge-m3",
      "mountPath": "/mnt/models/embedding"
    },
    {
      "id": "nomic-embed-text",
      "family": "embedding",
      "provider": "ollama",
      "runtimeRole": "embedding",
      "downloadable": true,
      "status": "available",
      "label": "Nomic Embed Text",
      "installCommand": "ollama pull nomic-embed-text",
      "mountPath": "/mnt/models/embedding"
    },
    {
      "id": "llama3.1:8b",
      "family": "llm",
      "provider": "ollama",
      "runtimeRole": "llm",
      "downloadable": true,
      "status": "available",
      "label": "Llama 3.1 8B",
      "installCommand": "ollama pull llama3.1:8b",
      "mountPath": "/mnt/models/llm"
    },
    {
      "id": "qwen2.5:7b",
      "family": "llm",
      "provider": "ollama",
      "runtimeRole": "llm",
      "downloadable": true,
      "status": "available",
      "label": "Qwen 2.5 7B",
      "installCommand": "ollama pull qwen2.5:7b",
      "mountPath": "/mnt/models/llm"
    },
    {
      "id": "whisper",
      "family": "audio",
      "provider": "whisper",
      "runtimeRole": "audio",
      "downloadable": false,
      "status": "available",
      "label": "Whisper Base",
      "installCommand": null,
      "mountPath": null
    }
  ]
}
```

Notes:

- `downloadable: true` models are Ollama-managed. `downloadable: false` models (OpenAI, Whisper) are verify-only; the operator manages their runtime externally.
- `provider` values: `"ollama"`, `"openai"`, `"whisper"`.
- `runtimeRole` values: `"embedding"`, `"llm"`, `"audio"`.

### POST `/api/models/download`

Queues an Ollama model download via Hangfire. Only `downloadable: true` catalog entries are accepted. Requires authentication.

Request:

```json
{
  "modelKind": "embedding",
  "modelId": "bge-m3"
}
```

Either `modelKind` (family, e.g. `"embedding"`) or `modelId` (e.g. `"bge-m3"`) may be provided; both are matched against the catalog. `modelKind` alone resolves the first matching family entry.

Response (202 Accepted):

```json
{
  "status": "queued",
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "statusUrl": "/api/admin/operations/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "modelKind": "embedding",
  "modelId": "bge-m3"
}
```

Error cases:

- `400 Bad Request` — catalog entry not found, or model is `downloadable: false` (verify-only).
- `503 Service Unavailable` — Hangfire is running on in-memory storage (PostgreSQL unavailable); the download cannot be durably persisted.

Notes:

- The 202 is only returned after the `operations` record and `model_runtime_state = queued` row are both persisted AND the Hangfire job is enqueued. There is no optimistic 202.
- Track progress via `GET /api/admin/operations/{operationId}` or the SSE stream `GET /api/models/events`.
- Download execution is worker-owned (pull concurrency 1). The API process never runs the Hangfire server.

### POST `/api/models/verify`

Runs a real presence/health probe against the model runtime and projects onboarding readiness from the result. Requires authentication.

- Ollama models: probed against `GET /api/tags` on the Ollama runtime.
- Whisper: probed via the audio-to-text service `/health` endpoint.
- OpenAI models: no local probe; returns a configuration advisory.

Verified presence writes `model_runtime_state.status = "ready"` and updates `last_verified_at` and `last_seen_in_runtime_at`.

Request:

```json
{
  "modelKind": "embedding",
  "modelId": "bge-m3"
}
```

Response (200 OK):

```json
{
  "status": "verified",
  "modelKind": "embedding",
  "modelId": "bge-m3",
  "verified": true,
  "message": "Model bge-m3 is present in the Ollama runtime."
}
```

On failure, `status` is `"failed"` and `verified` is `false`.

### GET `/api/models/status`

Returns the persisted `model_runtime_state` rows for all known models. This is the cross-process authoritative view of model lifecycle state. Requires authentication.

Response:

```json
{
  "models": [
    {
      "provider": "ollama",
      "modelId": "bge-m3",
      "runtimeRole": "embedding",
      "status": "ready",
      "currentOperationId": null,
      "progressPercent": null,
      "lastVerifiedAt": "2026-08-10T12:00:00Z",
      "lastSeenInRuntimeAt": "2026-08-10T12:00:00Z",
      "lastErrorSummary": null,
      "detailsJson": null,
      "updatedAt": "2026-08-10T12:00:00Z"
    }
  ]
}
```

`status` values (see §22.16):

- `queued` — download accepted, not yet executing.
- `running` — download in progress.
- `ready` — model present and verified in the runtime.
- `failed` — download or verification failed; see `lastErrorSummary`.
- `missing` — startup reconcile found no runtime record for a required model.
- `error` — unexpected error outside the normal pipeline.
- `verifying` — verify probe in flight.
- `downloading` — pull streaming in progress.

Notes:

- Use this endpoint for the initial page load and after every SSE reconnect to close event gaps. The SSE stream (`GET /api/models/events`) is an in-process hint stream; this endpoint is the cross-process truth (plan D5).

### GET `/api/models/events`

SSE stream of model-lifecycle events. WASM-friendly native `EventSource` semantics: plain GET, cookie-authenticated, no custom headers required. Requires authentication.

On connect, the server sends an initial `: connected` SSE comment before any events, so the client can confirm the connection is established.

**SSE event types:**

**`model.status`** — emitted on any `model_runtime_state` status transition except `ready` with an attached operation (which emits `operation.completed` instead) and `failed` with an attached operation (which emits `operation.failed` instead).

```
event: model.status
data: {
  "provider": "ollama",
  "modelId": "bge-m3",
  "runtimeRole": "embedding",
  "status": "running",
  "progressPercent": 42,
  "currentOperationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "lastVerifiedAt": null,
  "lastErrorSummary": null,
  "updatedAt": "2026-08-10T12:05:00Z"
}
```

**`operation.status`** — in-flight download progress update.

```
event: operation.status
data: {
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "provider": "ollama",
  "modelId": "bge-m3",
  "status": "running",
  "progressPercent": 42,
  "error": null
}
```

**`operation.completed`** — download finished and model is `ready`.

```
event: operation.completed
data: {
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "provider": "ollama",
  "modelId": "bge-m3"
}
```

**`operation.failed`** — download or pipeline error.

```
event: operation.failed
data: {
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "provider": "ollama",
  "modelId": "bge-m3",
  "error": "Model pull failed: connection refused"
}
```

Notes:

- JSON fields with null values are omitted from the serialized `data:` payload.
- The broadcaster is in-process (API process only). A stalled subscriber that falls behind the 256-event buffer is dropped; the client detects the closed stream and reconciles via `GET /api/models/status`.
- **D5 caveat:** The SSE stream is an in-process hint. For initial load and post-reconnect reconciliation, always call `GET /api/models/status` first.

### POST `/api/models/activate-embedding-model` *(MVP+)*

Changes the Active Embedding Model only after explicit confirmation. The pointer flips immediately, old-model embeddings become stale by derivation (ADR-0001), and the system enters Embedding Transition (ADR-0008) until the queued bulk reprocess completes.

Not yet implemented. Planned request shape:

```json
{
  "provider": "ollama",
  "model": "bge-m3",
  "confirmedRegeneration": true
}
```

### POST `/api/models/activate-llm-model` *(MVP+)*

Sets the active local LLM model after verification. Not yet implemented.

### POST `/api/models/activate-audio-model` *(MVP+)*

Sets the active audio-to-text model/engine after verification. Not yet implemented.

## 6. Channel endpoints

### GET `/api/channels`

Query params:

- `includePaused` boolean
- `page`
- `pageSize`

Response item may flatten effective values:

```json
{
  "id": "uuid",
  "youtubeChannelId": "UC...",
  "name": "Channel Name",
  "profileUrl": "https://www.youtube.com/channel/UC...",
  "isPaused": false,
  "isDegraded": false,
  "consecutiveFailures": 0,
  "lastIngestedAt": "2026-07-16T00:00:00Z",
  "lastIngestionStatus": "processed_with_warnings"
}
```

Channel state precedence is Deleted > Paused > Degraded > Active: a Paused channel is never probed or processed; a Degraded channel gets a single lightweight metadata probe per scheduled run, with a successful probe clearing the Degraded state (ADR-0003).

### POST `/api/channels`

Request:

```json
{
  "sourceUrl": "https://www.youtube.com/@example",
  "defaultMaxAgeDays": 30,
  "defaultBackfillMaxVideos": 100
}
```

Response includes resolved canonical channel metadata:

```json
{
  "id": "uuid",
  "youtubeChannelId": "UC...",
  "name": {
    "original": "Channel Name",
    "override": null,
    "effective": "Channel Name"
  },
  "description": {
    "original": "Description",
    "override": null,
    "effective": "Description"
  },
  "profileUrl": "https://www.youtube.com/channel/UC...",
  "sourceUrl": "https://www.youtube.com/@example",
  "isPaused": false,
  "ingestionDefaults": {
    "maxAgeDays": 30,
    "backfillMaxVideos": 100
  }
}
```

### GET `/api/channels/{channelId}`

Returns channel details with original/override/effective fields and recent ingestion status.

### PUT `/api/channels/{channelId}`

Request:

```json
{
  "nameOverride": "Preferred Name",
  "descriptionOverride": "Optional override",
  "isPaused": false,
  "defaultMaxAgeDays": 30,
  "defaultBackfillMaxVideos": 100
}
```

Response: immediate mutation.

### DELETE `/api/channels/{channelId}`

Query params:

- `deleteRelatedData` boolean, default false
- `confirm` string, required when deleting related data

Behavior:

- If `deleteRelatedData=false`, stop future ingestion/remove channel config according to implementation policy.
- If `true`, delete related videos/data/screenshots where safe.

## 7. Admin operations and ingestion endpoints

### GET `/api/admin/operations/{operationId}`

Returns application-owned operation status. The current admin operation surface exposes run, retry, reprocess, backup, and maintenance actions through this admin prefix.

```json
{
  "operationId": "uuid",
  "operationType": "retry.video",
  "status": "accepted",
  "message": "Retry queued for video 'uuid'.",
  "target": "uuid",
  "jobId": "hangfire-job-id",
  "healthStatus": null
}
```

All `POST /api/admin/operations/**` responses use this envelope. The HTTP status reflects the outcome: `200 OK` when the operation completes synchronously (`status` of `completed`/`ok`), `500` when it fails outright (`status` of `failed`/`error`, RFC 7807 problem details), and `202 Accepted` for queued/long-running work with `statusUrl` `/api/admin/operations/{operationId}`.

### POST `/api/admin/operations/ingestion/run`

Start manual ingestion for all active channels.

### POST `/api/admin/operations/ingestion/channel-backfill`

Start channel backfill for the configured scope.

### POST `/api/admin/operations/ingestion/runs/{runId}/retry`

Retry a failed ingestion run.

### POST `/api/admin/operations/videos/{videoId}/retry`

Retries failed/deferred stages of a video. Applies only to stages in a failed or deferred state (ADR-0002); requests naming succeeded stages are rejected per-stage with a validation error.

### POST `/api/admin/operations/links/{linkId}/retry`

Retries a failed link-related operation.

### POST `/api/admin/operations/repositories/{repositoryId}/retry`

Retries failed repository processing.

### POST `/api/admin/operations/videos/{videoId}/reprocess`

Re-runs the full ingestion pipeline for an already-succeeded video, bypassing the idempotency/skip guard (ADR-0002). A reprocess discovers fresh platform state, resets retry budgets, and marks affected search documents stale by derivation.

### POST `/api/admin/operations/repositories/{repositoryId}/reprocess`

Re-runs the full repository pipeline for a succeeded repository (ADR-0002). Refreshes metadata, README, LICENSE, and re-runs the DeepWiki check when appropriate.

### POST `/api/admin/operations/resources/{resourceId}/reprocess`

Re-runs the pipeline for a canonical resource.

### POST `/api/admin/operations/embeddings/reprocess`

Queues bulk reprocessing of embeddings for the requested scope.

### POST `/api/admin/operations/screenshots/purge`

Purges screenshot rows and files for the requested scope. Accepts an optional `target` query parameter (video id, channel id, or omit for the whole corpus). This is the shipped replacement for the previously-specced per-video/per-channel `DELETE` screenshot endpoints (see §14).

### POST `/api/admin/operations/embeddings/test`

Runs an embedding-service health/probe operation against the configured embedding runtime and reports the truthful outcome in the standard operation envelope.

### POST `/api/admin/operations/audio-to-text/test`

Runs an audio-to-text (whisper) health/probe operation against the configured service endpoint (`STREAMINGDIGEST_WHISPER_BASE_URL` / `whisper:baseUrl`) and reports the truthful outcome in the standard operation envelope. When whisper is unconfigured the operation completes with `healthStatus: "warning"` (degrade), not a fault.

### POST `/api/admin/operations/notifications/matrix/test`

Queues a Matrix notification test operation.

### POST `/api/admin/operations/backup`

Creates a backup archive and returns the resulting operation record.

### POST `/api/admin/operations/restore`

Restores the latest backup archive.

### GET `/api/admin/operations/backups/{archiveName}`

Downloads a backup archive.

### GET `/api/rate-limit-deferments`

Returns active and recent dependency deferments.

### POST `/api/rate-limit-deferments/{id}/clear`

Clears an active deferment manually. Use carefully; normal expiry follows `retryAfterAt`.

### POST `/api/batch/retry`

Queues retries for multiple retryable items such as ingestion items, videos, external resources, repositories, or notifications. Response uses the batch operation response shape. Items that have exhausted their Retry Budget are rejected per-item with `is_retryable = false`.

### POST `/api/batch/reprocess`

Queues full-pipeline reprocessing for multiple succeeded entities (videos, external resources, repositories). Staleness and embedding regeneration follow as downstream consequences (ADR-0002). Response uses the batch operation response shape.

### POST `/api/batch/delete`

Queues or performs deletion for multiple supported entities. Destructive batch deletes require explicit confirmation and should return per-item rejections for unsafe entities.

### Internal read-model endpoints

The dashboard and ingestion-run UI screens read from a set of internal, cookie-authenticated endpoints under `/api/internal`. These return the real stored projection of the database — there is no fixture/fallback path. They are consumed by the Blazor WASM client and are not part of the stable public contract.

#### GET `/api/internal/dashboard`

Returns the dashboard summary read model for the home screen: corpus counts, the latest ingestion run and its stored digest, failed/deferred item counts, the search launchpad, pending-action inbox items, and the waiting/corpus state used to render onboarding guidance. Assembled by the API from live channel/video/run/digest data.

#### GET `/api/internal/ingestion-runs`

Returns recent ingestion runs as summary cards (id, title, subtitle, status text), newest first.

Query params:

- `limit` (optional, integer 1–200, default `25`) — maximum runs to return; values are clamped into range.

#### GET `/api/internal/ingestion-runs/{ingestionRunId}`

Returns the full run detail view model: frozen run outcome (captured at completion), live rollup derived from current ingestion-item state, per-stage rollups, per-item details with retry metadata and per-item link/repository/website counts, active deferments, and deep links (Hangfire, notifications). Returns `404` when the run does not exist.

#### GET `/api/internal/ingestion-runs/{ingestionRunId}/notifications`

Returns the notification rows recorded for a run (digest summary / deferral notices), including provider, target room, status, attempt count, next retry time, provider message id, error summary, and timestamps. `retryable` is `true` for rows still in `pending`/`failed`.

## 8. Search endpoints

### POST `/api/search`

Request:

```json
{
  "query": "code project that searches for project ideas not yet achieved across all of github",
  "filters": {
    "channelIds": ["uuid"],
    "publishedFrom": "2026-01-01T00:00:00Z",
    "publishedTo": "2026-12-31T23:59:59Z",
    "matchedDocumentTypes": [
      "video_metadata",
      "segment_title",
      "transcript_chunk",
      "external_resource_metadata",
      "scraped_page_text",
      "repository_readme_chunk",
      "note"
    ],
    "hasTranscript": true,
    "hasRepo": true,
    "hasNotes": false,
    "ingestionStatuses": ["processed", "processed_with_warnings"]
  },
  "ranking": {
    "textWeight": 0.5,
    "vectorWeight": 0.5
  },
  "includeRelatedItems": true,
  "includeScoreDetails": true,
  "page": 1,
  "pageSize": 25
}
```

Response item is a video cluster search result. One video appears at most once per response page.

```json
{
  "items": [
    {
      "id": "video-cluster-result-uuid",
      "resultType": "video_cluster",
      "videoId": "uuid",
      "title": "Effective video title",
      "channel": {
        "id": "uuid",
        "name": "Channel Name",
        "url": "https://www.youtube.com/channel/UC..."
      },
      "publishedAt": "2026-07-16T00:00:00Z",
      "score": 0.87,
      "relativeSimilarityPercent": 87,
      "scoreComponents": {
        "maxDocumentScore": 0.91,
        "averageTop3DocumentScore": 0.82,
        "coverageScore": 0.75,
        "noteBoost": 0.08,
        "interactionBoost": 0.02,
        "textWeight": 0.5,
        "vectorWeight": 0.5,
        "matchedDocumentTypes": ["transcript_chunk", "repository_readme_chunk"]
      },
      "primaryMatch": {
        "documentId": "uuid",
        "documentType": "transcript_chunk",
        "sourceEntityType": "segment",
        "sourceEntityId": "uuid",
        "matchedField": "body",
        "snippet": "...",
        "startSeconds": 754,
        "watchUrl": "https://www.youtube.com/watch?v=abc123&t=754s",
        "screenshotUrl": "/api/screenshots/uuid"
      },
      "matchesInsideCount": 12,
      "matches": [],
      "relatedItems": [
        {
          "type": "video_cluster",
          "id": "uuid",
          "title": "Related item",
          "relativeSimilarityPercent": 81
        }
      ],
      "links": {
        "watch": "https://www.youtube.com/watch?v=abc123&t=754s",
        "channel": "https://www.youtube.com/channel/UC...",
        "repositories": ["https://github.com/owner/repo"],
        "websites": ["https://example.com"]
      },
      "warnings": [
        {
          "code": "website_scrape_failed",
          "message": "One linked website could not be scraped."
        }
      ],
      "hasNotes": true,
      "processingStatus": "processed_with_warnings"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 100,
  "queryDiagnostics": {
    "embeddingProvider": "ollama",
    "embeddingModel": "bge-m3",
    "rankingFormulaVersion": "mvp-1",
    "relativeSimilarityExplanation": "Relative to the active query, model, and pre-pagination candidate set; not confidence.",
    "embeddingTransition": {
      "inTransition": false,
      "coveragePercent": 100
    }
  }
}
```

During an Embedding Transition (ADR-0008), vector search covers only Active-Embedding-Model embeddings and `embeddingTransition` reports rebuild progress; the UI shows a "search coverage rebuilding" banner rather than silently returning sparse results.

`relativeSimilarityPercent` is calculated over the pre-pagination candidate set, defaulting to the top 200 vector candidates before hybrid aggregation.

MVP search filters intentionally exclude link-classification filtering and hide/show-by-category behavior. External resource classifications are still returned on resource/link detail responses, and classification correction remains MVP because it improves future classification and ranking quality. Search request filters such as `linkClassifications` are MVP+.

Until the first ingestion run completes with at least one ingested video, the search UI is blocked by a pre-corpus waiting state (PRD §2.10); the endpoint remains available but the client never offers search as a mode before the corpus exists.

### GET `/api/search/suggestions`

Optional query suggestions/facets.

Query params:

- `q`
- `limit`

### GET `/api/search-documents/stale`

Returns currently-stale search documents for diagnostics/admin repair. Staleness is computed (content-hash or model mismatch, ADR-0001), not read from a flag.

### POST `/api/search-documents/{id}/reprocess`

Queues reprocessing for a single search document and its embedding. Response: accepted operation.

### GET `/api/vector-index/status`

Returns pgvector/index/provider status and active model/dimension metadata.

## 9. Recent searches and user interaction endpoints

Recent searches are a product primitive: they power the recent-search panel, high-signal daily digest matching, and ranking personalization.

### GET `/api/recent-searches`

Query params:

- `page`
- `pageSize`
- `sort`, default `searchedAt`
- `direction`, default `desc`

Response item:

```json
{
  "id": "uuid",
  "queryText": "code project ideas across github",
  "searchedAt": "2026-07-17T10:00:00Z",
  "textWeight": 0.5,
  "vectorWeight": 0.5,
  "filters": {},
  "embeddingStatus": "succeeded",
  "highSignalMatchCount": 3
}
```

### DELETE `/api/recent-searches`

Clear all recent-search history. MVP supports clear-all only; granular per-query deletion is MVP+.

Behavior:

- Deletes `recent_searches` rows and their query embeddings.
- Does not delete user interaction events or other historical domain events unless future privacy tooling explicitly supports that.
- Returns immediate mutation with counts.

Response:

```json
{
  "status": "deleted",
  "deletedRecentSearchCount": 42,
  "deletedQueryEmbeddingCount": 42
}
```

### POST `/api/user-interaction-events`

Records clicked/opened result signals used for future ranking boosts and high-signal digest tuning.

Request:

```json
{
  "recentSearchId": "uuid",
  "videoId": "uuid",
  "searchDocumentId": "uuid",
  "resultType": "video_cluster",
  "eventType": "timestamp_opened",
  "metadata": {
    "rank": 1,
    "targetUrl": "https://www.youtube.com/watch?v=abc123&t=754s"
  }
}
```

Response:

```json
{
  "id": "uuid",
  "status": "recorded",
  "activatedAt": "2026-07-17T10:00:00Z"
}
```

The client should call this endpoint when the user opens a result, timestamp, repository, website, or note. Redirect-based implicit tracking is MVP+.

## 10. Video endpoints

### GET `/api/videos/{videoId}`

Returns video metadata with original/override/effective values, segments, link occurrences, canonical resources, repositories, transcript status, processing status, and notes summary.

### GET `/api/videos/{videoId}/segments`

Returns active segment list with screenshots. May include inactive generations when requested by query param.

Query params:

- `includeInactiveGenerations` boolean

### GET `/api/videos/{videoId}/transcript`

Returns transcript cues/chunks.

### GET `/api/videos/{videoId}/links`

Returns extracted link occurrences, canonical external resources, classifications, and processing status.

### GET `/api/videos/{videoId}/search-documents`

Returns generated search documents for diagnostics.

### GET `/api/videos/{videoId}/events`

Returns domain events/warnings for the video.

### GET `/api/videos/{videoId}/processing-status`

Returns current processing/staleness/retry state.

### DELETE `/api/videos/{videoId}`

Query params:

- `deleteScreenshots` boolean, default true
- `confirm` string

## 11. Edit/override endpoints

All override endpoints return the immediate mutation shape and include effective values, stale search documents, stale cluster embeddings, and queued reprocess operations.

### PUT `/api/videos/{videoId}/overrides`

Request:

```json
{
  "author": "Override author",
  "title": "Override title",
  "description": "Override description"
}
```

### PUT `/api/segments/{segmentId}/overrides`

Request:

```json
{
  "title": "Override segment title",
  "summary": "Override summary"
}
```

### PUT `/api/transcript-cues/{cueId}/overrides`

Request:

```json
{
  "text": "Corrected transcript text"
}
```

### PUT `/api/external-resources/{resourceId}/overrides`

Request:

```json
{
  "title": "Override title",
  "description": "Override description",
  "classification": "website_resource"
}
```

If classification changes, create a `classification_corrections` entry and mark affected search documents stale.

### PUT `/api/repositories/{repositoryId}/overrides`

Request:

```json
{
  "description": "Override repo description",
  "primaryLanguage": "C#",
  "topics": ["search", "youtube", "postgres"]
}
```

### GET `/api/overrides/history`

Query params:

- `entityType`
- `entityId`
- `fieldName`

## 12. Notes endpoints

### GET `/api/notes`

Query params:

- `targetType`
- `targetId`

### POST `/api/notes`

MVP allows one note per target: creating when a live note already exists returns `409 Conflict`; use `PUT` to edit the existing note. Multiple notes per target are MVP+.

Request:

```json
{
  "targetType": "segment",
  "targetId": "uuid",
  "title": "Idea",
  "markdown": "Use this for..."
}
```

Response includes embedding/search status:

```json
{
  "id": "uuid",
  "targetType": "segment",
  "targetId": "uuid",
  "title": "Idea",
  "markdown": "Use this for...",
  "embeddingStatus": "stale",
  "updatedAt": "2026-07-17T10:00:00Z",
  "queuedOperations": []
}
```

### GET `/api/notes/{noteId}`

### PUT `/api/notes/{noteId}`

Request:

```json
{
  "title": "Updated title",
  "markdown": "Updated markdown"
}
```

### DELETE `/api/notes/{noteId}`

Soft-deletes a note by setting `deletedAt`/`deleted_at`, clearing it from normal note lists and search results. The note's derived search document and embedding are hard-deleted (staleness means "source changed, regenerate" — a deleted source has nothing to regenerate to), and the parent video-cluster aggregate is marked stale for rebuild.

MVP semantics:

- Default behavior is soft delete.
- Hard purge is not exposed as a normal note endpoint in MVP.
- Admin/privacy hard purge is MVP+ or can be implemented later as a maintenance operation.

Response: immediate mutation.

## 13. Embedding endpoints

### POST `/api/embeddings/reprocess`

Bulk reprocess of embeddings, typically after an embedding model change (this is the operation queued by `activate-embedding-model`, ADR-0008).

Request:

```json
{
  "model": "bge-m3",
  "onlyStale": false
}
```

Response: accepted operation.

### POST `/api/embeddings/reprocess-item`

Request:

```json
{
  "sourceEntityType": "segment",
  "sourceEntityId": "uuid"
}
```

Response: accepted operation.

### GET `/api/embeddings/status`

Returns model, dimensions, computed stale count, failed count, last reprocess time, active operation if present, and embedding-transition state (`inTransition`, `coveragePercent`).

## 14. Screenshot/media endpoints

### GET `/api/screenshots/{screenshotId}`

Returns the WebP file. When the file is missing from the volume (or the row is marked failed), returns a stable placeholder image instead of a 404, so result cards keep a fixed layout; the missing file is recorded as a domain event and the row becomes retryable as an Enrichment Stage failure.

### Purging screenshots

Screenshot purges are performed through the admin operations surface, not per-entity `DELETE` endpoints:

### POST `/api/admin/operations/screenshots/purge`

Purges screenshot rows and generated files for the requested scope. Query params:

- `target` (optional): a video id or channel id to scope the purge; omit to purge the whole corpus.

Response: accepted operation (track `/api/admin/operations/{operationId}`) or `200 OK` when the purge completes synchronously.

The previously-specced `DELETE /api/videos/{videoId}/screenshots` and `DELETE /api/channels/{channelId}/screenshots` routes are **not implemented**; use this operation instead.

## 15. Repository endpoints

### GET `/api/repositories/{repositoryId}`

Returns metadata, README status, LICENSE status, DeepWiki URL, source resources/videos, and original/override/effective editable fields. The Repository is the single source for repository metadata and overrides (ADR-0009): a linked external resource classified `code_repository` delegates its title/description effective values to the Repository, and its own metadata override fields are ignored for display/search while the association exists.

### GET `/api/repositories/{repositoryId}/documents`

Returns README/LICENSE documents.

### GET `/api/repositories/{repositoryId}/source-videos`

Returns videos/link occurrences that reference the repository.

### POST `/api/repositories/{repositoryId}/check-deepwiki`

Queues a DeepWiki check. Re-check is meaningful only when the stored outcome was negative (no page or placeholder); a stored reachable DeepWiki URL is not re-verified in MVP. Response: accepted operation.

## 16. External resource and link occurrence endpoints

### GET `/api/external-link-occurrences/{occurrenceId}`

Returns occurrence context, source video, original URL, normalized URL, and linked canonical resource.

An occurrence may temporarily have `externalResourceId = null` while redirect resolution/canonicalization is pending or failed. In that case the response must include `resolutionStatus` and `resolutionErrorSummary` so the UI can show an incomplete-processing warning rather than hiding the occurrence.

Example unresolved fields:

```json
{
  "id": "uuid",
  "externalResourceId": null,
  "resolutionStatus": "pending",
  "resolutionErrorSummary": null
}
```

### GET `/api/external-resources/{resourceId}`

Returns canonical external resource details, classification, scrape status, repo association if any, and source occurrences.

### GET `/api/external-resources/{resourceId}/occurrences`

Returns all known video/source occurrences for a canonical resource.

### POST `/api/external-resources/{resourceId}/reclassify`

Queues local LLM reclassification using current correction history (active Corrections only, ADR-0007).

### POST `/api/external-resources/{resourceId}/reprocess`

Re-runs resource processing (scrape/classify/repository association) for a succeeded resource, bypassing the idempotency guard (ADR-0002). Response: accepted operation.

## 17. Admin health/test endpoints

> ⚠️ **Status: Legacy endpoints documented below are not implemented in shipped code.**
> Test functionality has been consolidated under `/api/admin/operations/` (§7).
> This section documents legacy routes from pre-MVP planning; see issue #251.
> **NEW in #270:** `/api/admin/health` is now implemented with live signal support (see below).

### GET `/api/admin/health` (Live Implementation)

Returns comprehensive live and preview health status for admin settings page and operations dashboard.
Endpoint responses include a `PreviewMode` flag per section to distinguish authoritative live signals from static/expected data.
Reference: ADR-0018 (admin-health-contract-live-vs-preview-signals.md).

**Request:**
```
GET /api/admin/health
Authorization: Cookie (session required)
```

**Response (200 OK — All systems ready):**
```json
{
  "settings": {
    "state": "Ready",
    "summary": "Version 1.0.0 ready",
    "details": [],
    "previewMode": false
  },
  "models": {
    "state": "Ready",
    "summary": "All models operational",
    "models": [
      {
        "name": "embedding",
        "state": "Ready",
        "status": "ready",
        "version": "nomic-embed-text:latest",
        "details": "100% complete"
      }
    ],
    "activeOperationCount": 0,
    "previewMode": false
  },
  "observability": {
    "state": "Ready",
    "summary": "Telemetry collection and export operational",
    "tracesStatus": "Operational",
    "metricsStatus": "Operational",
    "logsStatus": "Operational",
    "details": [],
    "previewMode": false
  },
  "storage": {
    "state": "Ready",
    "summary": "PostgreSQL database operational with pgvector enabled",
    "postgresStatus": "Ready",
    "details": [
      "PostgreSQL 16.3",
      "pgvector 0.8.0",
      "Database latency: 2ms"
    ],
    "previewMode": false
  },
  "backupReadiness": {
    "state": "Ready",
    "summary": "Backup system operational (live verification pending)",
    "lastBackupAt": null,
    "timeSinceLastBackup": "Unknown",
    "retentionStatus": "Not yet verified",
    "details": ["Backup verification is preview state; live verification is pending."],
    "previewMode": true
  },
  "overallHealth": "Ready",
  "lastUpdatedAt": "2026-08-15T22:00:00Z"
}
```

**Response (202 Accepted — Service reconnecting):**
Same schema as above, but HTTP 202 indicates one or more services are recovering from connectivity issues.
Caller should retry with exponential backoff.

**Response (503 Service Unavailable — Critical failure):**
One or more critical services (database, core observability pipeline, or model runtime) is unavailable.
Returned as plain text error detail; payload is not guaranteed.

**Response (500 Internal Server Error):**
Unexpected error generating health snapshot. Retry after delay; investigate logs.

**Signal Definitions (all backed by live runtime state unless PreviewMode=true):**

| Section | Source | Live | Details |
|---------|--------|------|---------|
| Settings | `UpgradeCompatibilityStateService.ReadVersionStateAsync()` + database | Yes | App version + DB schema version from app_version table |
| Models | `IModelRuntimeStateRepository.GetAllAsync()` + model_runtime_states table | Yes | Same source as `/api/models/status` (embedded or audio runtime models) |
| Observability | `CompositeServiceHealthProvider.ProbeAllAsync()` + TelemetryProbe | Yes | Traces, metrics, logs collection and OTLP export readiness |
| Storage | `CompositeServiceHealthProvider.ProbeAllAsync()` + PostgresProbe | Yes | PostgreSQL connectivity, latency, pgvector extension presence |
| BackupReadiness | `UpgradeMaintenanceSnapshotService` (hardcoded demo) | **No** | Static placeholder; live backup manifest verification pending #271 |

**PreviewMode Flag Semantics:**

- `previewMode: false` (default): Signal is authoritative and backed by live runtime APIs, database queries, or active health probes. Operator should act on this state for operational decisions (e.g., alerting, capacity planning).
- `previewMode: true`: Signal is expected/demo state and NOT live-verified. UI should render with (?) badge and tooltip: "This signal is preview state; live verification is pending." Operator should not depend on this for operational decisions until live implementation lands.

**Health State Enum:**

| State | Code | Meaning |
|-------|------|---------|
| Ready | 0 | Fully operational |
| Degraded | 1 | Operational with warnings (e.g., high latency, partial failure) |
| Reconnecting | 2 | Recovering from transient connectivity issue |
| Paused | 3 | Admin-paused (e.g., during maintenance) |
| Error | 4 | Not operational (unrecoverable failure) |

**Authorization & Auditability:**

- Requires authenticated session (cookie-based).
- Debug logging at `Debug` level includes per-section probe details and timing.
- No PII or secrets in response or logs (except correlation ID).

**Related:**

- ADR-0018: Admin health contract and live vs. preview signals
- #270: Define the live admin health contract
- #271: Replace static maintenance snapshot with live backend health data (pending)
- #272: Guarantee model and observability status signals propagate reliably via SSE (pending)

### ~~POST `/api/admin/test-matrix`~~ (NOT IMPLEMENTED)

Use `POST /api/admin/operations/notifications/matrix/test` instead (§7).

### ~~POST `/api/admin/test-embedding`~~ (NOT IMPLEMENTED)

Use `POST /api/admin/operations/embeddings/test` instead (§7).

### ~~POST `/api/admin/test-audio-to-text`~~ (NOT IMPLEMENTED)

Use `POST /api/admin/operations/audio-to-text/test` instead (§7).
Performs a real `GET /health` probe against the configured whisper service
(`STREAMINGDIGEST_WHISPER_BASE_URL`) via `IAudioToTextProvider.CheckHealthAsync`
and reports the truthful status. The previous behavior (a fake `completed`/`healthy`
without probing) has been removed (issue #210).

Response shape is the standard admin-action-result envelope. The `status`,
`healthStatus`, and `message` fields reflect the probe outcome:

| Probe outcome | `status` | `healthStatus` | `message` |
| --- | --- | --- | --- |
| `/health` returned 2xx | `completed` | `healthy` | Engine + endpoint + "succeeded" |
| Whisper unavailable (no runtime, no provider registered, 5xx, connection refused, stub) | `completed` | `warning` | Engine + endpoint + "unavailable" + degrade note |
| Probe threw (genuine fault) | `failed` | `error` | Failure detail |

When whisper is unavailable, caption-less videos degrade to `unavailable_captions`
with a `transcript_ingest_failed` domain event (notify); captioned ingestion proceeds
with a warning (PRD §2.4). An unconfigured whisper runtime (no `IAudioToTextProvider`
registered) is treated as the expected degrade state, not a fault, so it returns
`completed`/`warning` (HTTP 200); HTTP 500 is reserved for genuine probe exceptions only.

### ~~POST `/api/admin/test-scraper`~~ (NOT IMPLEMENTED)

Documented as running scraper health test against a controlled URL or fixture.
Not shipped; no equivalent endpoint exists.

### ~~POST `/api/admin/test-repository-provider`~~ (NOT IMPLEMENTED)

Documented as running repository-provider health test, GitHub for MVP.
Not shipped; no equivalent endpoint exists.

### ~~POST `/api/admin/test-youtube-ingestion`~~ (NOT IMPLEMENTED)

Documented as running lightweight YouTube adapter verification without ingesting a full video.
Not shipped; no equivalent endpoint exists.

### ~~GET `/api/admin/observability-links`~~ (NOT IMPLEMENTED)

Documented as returning Grafana, Prometheus, Hangfire, Loki, and Tempo URLs.
Not shipped; observability URLs are available via environment configuration instead.

## 18. Backup and upgrade/maintenance endpoints

> ⚠️ **Status: Most of these endpoints are not implemented in shipped code.**
> Only backup/restore operations are available under `/api/admin/operations/`.
> This section documents planned maintenance/upgrade workflows (MVP+ scope).
> See issue #251.

MVP maturity rule (design intent, not yet shipped):

- Status, versions, backup creation/list/detail, upgrade preview, and derived-data regeneration endpoints are planned MVP contracts.
- `apply-migrations` is a planned MVP contract for app/config/DB migrations that are safe to run in-app after compatibility checks pass.
- High-risk infrastructure migrations, such as PostgreSQL major upgrades, pgvector extension upgrades, large volume moves, or Matrix crypto-store migrations after E2EE is enabled, must return a manual/guided runbook requirement instead of attempting unsafe fully automated mutation.

### ~~GET `/api/admin/maintenance/status`~~ (NOT IMPLEMENTED)

Planned to return versions, compatibility, backup status, migration status, derived-data status, and post-upgrade checklist state. Not shipped.

### ~~GET `/api/admin/maintenance/versions`~~ (NOT IMPLEMENTED)

Planned to return current app, DB schema, config schema, and deployment schema versions. Not shipped.

### POST `/api/admin/operations/backup` (SHIPPED)

See `/api/admin/operations/backup` under §7. Starts backup operation.

### ~~GET `/api/admin/backups`~~ (NOT IMPLEMENTED)

Planned to list backup artifacts. Use `/api/admin/operations/backups/{archiveName}` to retrieve a specific backup file.

### ~~GET `/api/admin/backups/{backupId}`~~ (NOT IMPLEMENTED)

Planned to return backup artifact detail. Not shipped.

### ~~POST `/api/admin/backups/{backupId}/verify`~~ (NOT IMPLEMENTED)

Planned to queue backup verification/dry-run checks. Not shipped.

### ~~GET `/api/admin/upgrade/preview`~~ (NOT IMPLEMENTED)

Planned to return migration/config/deployment/derived-data preview and risk level. Not shipped.

### ~~POST `/api/admin/upgrade/apply-migrations`~~ (NOT IMPLEMENTED)

Planned to apply allowed app/config/DB migrations when deployment compatibility checks pass.
High-risk infrastructure migrations should point to guided/manual runbooks. Not shipped.

### ~~POST `/api/admin/derived-data/reprocess`~~ (NOT IMPLEMENTED)

Planned to queue reprocessing of stale search documents, embeddings, aggregate vectors, or index rebuilds.
Use `POST /api/admin/operations/embeddings/reprocess` (§7) for embedding reprocessing.

## 19. Matrix notification dispatch (shipped: in-process)

The shipped implementation dispatches Matrix notifications **in-process** from the worker/API via `INotificationDispatchService` (notification row + outbox message in PostgreSQL, dispatched to `IMatrixNotificationService`). It does not expose a separate HTTP internal Matrix service on the Compose network. The request/response contract below describes the logical ingestion-summary payload that is stored on the `Notification` row and rendered for Matrix; a future extraction to a standalone internal Matrix HTTP service would reuse this shape.

The digest notification record written for a run is `notificationType = "ingestion_summary"`, `provider = "matrix"`. `target` is the literal string `"matrix"` when the default configured room is used, otherwise a room override id. The Matrix message body is a plain-text rendering of the stored run digest, e.g. `Streaming Digest {runType} run {runId:N}: 4 new videos, 2 new resources, 1 high-signal match, 0 failed items, 0 skipped items, 0 active deferrals`. Sends go through the transactional outbox; a failed or unconfigured send leaves the row `pending` and retries at 5-minute intervals, surfacing on the dashboard/notifications view rather than failing the run. MVP Matrix sends are not required to be E2EE; E2EE is MVP+.

### POST `/internal/matrix/send-ingestion-summary` *(logical contract; in-process in MVP)*

Request:

```json
{
  "ingestionRunId": "uuid",
  "roomId": "!room:matrix.org",
  "summary": {
    "channelsChecked": 10,
    "newVideosFound": 4,
    "videosIngested": 3,
    "videosFailed": 1,
    "videosSkipped": 0,
    "transcriptsFound": 3,
    "transcriptsMissing": 1,
    "repositoriesFound": 2,
    "dashboardUrl": "https://streaming-digest/admin/ingestion/runs/uuid"
  }
}
```

Response:

```json
{
  "status": "sent",
  "providerMessageId": "$event"
}
```

MVP+ E2EE detail:

- The notifier must persist Matrix crypto/session state on a mounted volume.
- Manual device verification is required before encrypted-room readiness becomes green.
- Losing the crypto/session store may require re-verification and may make historical encrypted messages unreadable by the bot.
- Encrypted-send verification must be part of backup/restore validation when E2EE is enabled.

## 20. Scraper internal API

### POST `/internal/scrape/first-page`

Request:

```json
{
  "url": "https://example.com/page",
  "respectRobotsTxt": true,
  "debugCaptureRawHtml": false,
  "timeoutSeconds": 30
}
```

Response:

```json
{
  "finalUrl": "https://example.com/page",
  "title": "Page title",
  "description": "Page description",
  "openGraph": {},
  "visibleText": "Extracted text...",
  "robotsAllowed": true,
  "httpStatus": 200,
  "contentType": "text/html",
  "contentHash": "sha256:...",
  "rawHtmlDebugPath": null
}
```

## 21. Audio-to-text internal API

### POST `/internal/audio-to-text/models/download`

Executes configured CLI download/use command against the mounted model volume. This also supports user-provided host model paths mounted into the container.

### POST `/internal/audio-to-text/transcribe`

Input:

- multipart audio file, or internal file path reference if service shares temp volume.

Response:

```json
{
  "engine": "whisper.cpp",
  "model": "base.en",
  "language": "en",
  "durationSeconds": 600,
  "text": "Full transcript...",
  "cues": [
    {
      "startSeconds": 0.0,
      "endSeconds": 4.2,
      "text": "Hello..."
    }
  ]
}
```

## 22. API enum appendix

The following string values are the MVP contract vocabulary. Servers may add new values later; clients should render unknown values safely.

### 22.1 Common status values

Generic operation-like statuses:

- `not_started`
- `pending`
- `queued`
- `running`
- `succeeded`
- `completed`
- `completed_with_warnings`
- `warning`
- `failed`
- `cancelled`
- `skipped`
- `deferred`
- `stale`
- `requires_user_approval`

### 22.2 Ingestion run/item statuses

- `pending`
- `queued`
- `processing`
- `processed`
- `processed_with_warnings`
- `failed`
- `skipped`
- `deferred`
- `cancelled`

### 22.3 Ingestion run types

- `scheduled`
- `manual`
- `backfill`

### 22.4 Retryable ingestion stages

- `metadata`
- `transcript`
- `audio_transcription`
- `segmentation`
- `screenshots`
- `link_extraction`
- `link_classification`
- `repository_metadata`
- `repository_readme`
- `repository_license`
- `deepwiki_check`
- `website_scrape`
- `search_documents`
- `embeddings`
- `notification`

### 22.5 Search document types / `matchedDocumentTypes`

- `video_metadata`
- `segment_title`
- `transcript_chunk`
- `external_resource_metadata`
- `scraped_page_text`
- `repository_readme_chunk`
- `note`

### 22.6 Top-level search result types

- `video_cluster`

MVP search results are video-clustered. Segment/repository/link/note matches appear inside a video cluster rather than as independent top-level result types.

### 22.7 External resource classifications

- `code_repository`
- `website_resource`
- `ad_sponsor`
- `affiliate`
- `social`
- `newsletter`
- `course`
- `merch`
- `unknown`
- `other`

### 22.8 External resource types

- `repository`
- `website`
- `social`
- `document`
- `unknown`

### 22.9 Note/search target types

- `video`
- `segment`
- `external_resource`
- `repository`

### 22.10 User interaction event types

- `result_opened`
- `timestamp_opened`
- `repository_opened`
- `website_opened`
- `note_opened`

### 22.11 Operation types

- `ingestion_run`
- `retry_stage`
- `reprocess`
- `reprocess_embeddings`
- `segment_regeneration`
- `backup`
- `migration`
- `model_download`
- `health_check`
- `derived_data_regeneration`
- `screenshot_purge`

### 22.12 Readiness check keys

- `admin_password_changed`
- `embedding_model_verified`
- `llm_model_verified`
- `audio_to_text_verified`
- `matrix_verified`
- `observability_verified`
- `first_channel_added`
- `schedule_confirmed`
- `backup_path_verified`

### 22.13 Backup types

- `full`
- `db_only`
- `media_only`
- `config_only`

### 22.14 Risk levels

- `safe`
- `low`
- `medium`
- `high`
- `critical`

### 22.15 Sort directions

- `asc`
- `desc`

### 22.16 Model runtime state statuses

Written to `model_runtime_state.status` by the download pipeline (WS-2/WS-3/WS-4/WS-5) and read by the readiness guard (WS-7):

- `queued` — download accepted and persisted; waiting for worker pickup.
- `running` — Ollama pull streaming in progress; `progressPercent` is populated.
- `ready` — model is present and verified in the runtime.
- `failed` — download or post-pull presence check failed; `lastErrorSummary` is populated.
- `missing` — startup reconcile found no record for a required model.
- `error` — unexpected error outside the normal pipeline.
- `verifying` — verify probe in flight.
- `downloading` — pull in progress (alias used by some write paths).

## 23. Versioning

MVP can use unversioned `/api` paths. Add `/api/v1` before external/public API commitments if the app grows beyond personal use.
