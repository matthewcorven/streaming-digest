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

Returns supported installation/configuration options for embedding, LLM, and audio-to-text models, including displayed CLI download commands and mounted-path hints. MVP uses a hard-coded supported list rather than live hardware viability detection.

### POST `/api/models/download`

Queues model download/setup through an internal service.

Request:

```json
{
  "modelKind": "embedding",
  "modelId": "bge-m3"
}
```

Response: accepted operation.

### POST `/api/models/verify`

Verifies the configured or provided model.

Request:

```json
{
  "modelKind": "embedding",
  "modelId": "bge-m3"
}
```

### POST `/api/models/activate-embedding-model`

Changes the Active Embedding Model only after explicit confirmation. The pointer flips immediately, old-model embeddings become stale by derivation (ADR-0001), and the system enters Embedding Transition (ADR-0008) until the queued bulk reprocess completes.

```json
{
  "provider": "ollama",
  "model": "bge-m3",
  "confirmedRegeneration": true
}
```

Response:

```json
{
  "status": "accepted",
  "modelChanged": true,
  "staleEmbeddingCount": 12492,
  "operationId": "uuid",
  "statusUrl": "/api/admin/operations/{operationId}"
}
```

### POST `/api/models/activate-llm-model`

Sets the active local LLM model after verification.

### POST `/api/models/activate-audio-model`

Sets the active audio-to-text model/engine after verification.

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

### DELETE `/api/videos/{videoId}/screenshots`

Purge screenshots for video. Response: accepted operation or immediate mutation depending implementation.

### DELETE `/api/channels/{channelId}/screenshots`

Purge screenshots for channel. Response: accepted operation.

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

### GET `/api/admin/health`

Returns dependency health summary:

- PostgreSQL.
- pgvector.
- Ollama embeddings.
- Ollama LLM.
- audio-to-text service.
- scraper service.
- Matrix notifier.
- Hangfire.

### POST `/api/admin/test-matrix`

Sends Matrix test notification. MVP sends do not require E2EE. Encrypted/E2EE sends are MVP+.

### POST `/api/admin/test-embedding`

Request:

```json
{
  "text": "test embedding"
}
```

Returns model/dimensions and latency.

### POST `/api/admin/test-audio-to-text`

Performs a real `GET /health` probe against the configured whisper service (`STREAMINGDIGEST_WHISPER_BASE_URL`) via `IAudioToTextProvider.CheckHealthAsync` and reports the truthful status. The previous behavior (a fake `completed`/`healthy` without probing) has been removed (issue #210).

Response shape is the standard admin-action-result envelope. The `status`, `healthStatus`, and `message` fields reflect the probe outcome:

| Probe outcome | `status` | `healthStatus` | `message` |
| --- | --- | --- | --- |
| `/health` returned 2xx | `completed` | `healthy` | Engine + endpoint + "succeeded" |
| Whisper unavailable (no runtime, 5xx, connection refused, stub) | `completed` | `warning` | Engine + endpoint + "unavailable" + degrade note |
| No `IAudioToTextProvider` registered / probe threw | `failed` | `error` | Failure detail |

When whisper is unavailable, caption-less videos degrade to `unavailable_captions` with a `transcript_ingest_failed` domain event (notify); captioned ingestion proceeds with a warning (PRD §2.4).

### POST `/api/admin/test-scraper`

Runs scraper health test against a controlled URL or fixture.

### POST `/api/admin/test-repository-provider`

Runs repository-provider health test, GitHub for MVP.

### POST `/api/admin/test-youtube-ingestion`

Runs lightweight YouTube adapter verification without ingesting a full video when possible.

### GET `/api/admin/observability-links`

Response:

```json
{
  "grafanaUrl": "http://host:3000",
  "prometheusUrl": "http://host:9090",
  "hangfireUrl": "/admin/jobs",
  "lokiUrl": "http://host:3100",
  "tempoUrl": "http://host:3200"
}
```

## 18. Backup and upgrade/maintenance endpoints

MVP maturity rule:

- Status, versions, backup creation/list/detail, upgrade preview, and derived-data regeneration endpoints are real MVP contracts.
- `apply-migrations` is an MVP contract for app/config/DB migrations that are safe to run in-app after compatibility checks pass.
- High-risk infrastructure migrations, such as PostgreSQL major upgrades, pgvector extension upgrades, large volume moves, or Matrix crypto-store migrations after E2EE is enabled, must return a manual/guided runbook requirement instead of attempting unsafe fully automated mutation.

### GET `/api/admin/maintenance/status`

Returns versions, compatibility, backup status, migration status, derived-data status, and post-upgrade checklist state.

```json
{
  "versions": {
    "appVersion": "0.1.0",
    "dbSchemaVersion": "202607170001",
    "configSchemaVersion": "1",
    "deploymentSchemaVersion": "1"
  },
  "riskLevel": "medium",
  "backup": {
    "lastBackupAt": "2026-07-17T10:00:00Z",
    "backupRecommended": true,
    "backupRequired": false
  },
  "compatibility": {
    "api": "healthy",
    "worker": "paused_until_migration_complete",
    "postgres": "healthy",
    "ollama": "healthy",
    "whisper": "warning",
    "matrix": "not_configured"
  },
  "derivedData": {
    "staleSearchDocuments": 123,
    "staleEmbeddings": 456,
    "pendingSegmentApprovals": 0
  }
}
```

### GET `/api/admin/maintenance/versions`

Returns current app, DB schema, config schema, and deployment schema versions.

### POST `/api/admin/backups`

Starts MVP server-side backup.

Request:

```json
{
  "backupType": "full",
  "offerDownload": true
}
```

Response: accepted operation.

### GET `/api/admin/backups`

Lists backup artifacts.

### GET `/api/admin/backups/{backupId}`

Returns backup artifact detail.

### POST `/api/admin/backups/{backupId}/verify`

Queues backup verification/dry-run checks where practical.

### GET `/api/admin/upgrade/preview`

Returns migration/config/deployment/derived-data preview and risk level.

### POST `/api/admin/upgrade/apply-migrations`

Applies allowed app/config/DB migrations when deployment compatibility checks pass. High-risk infrastructure migrations should point to guided/manual runbooks.

### POST `/api/admin/derived-data/reprocess`

Queues reprocessing of stale search documents, embeddings, aggregate vectors, or index rebuilds.

## 19. Matrix notifier internal API

This API should be internal to the Compose network. MVP Matrix sends are not required to be E2EE; E2EE is MVP+.

### POST `/internal/matrix/send-ingestion-summary`

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

## 23. Versioning

MVP can use unversioned `/api` paths. Add `/api/v1` before external/public API commitments if the app grows beyond personal use.
