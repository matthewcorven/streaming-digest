# Streaming Digest REST API Spec

Status: MVP API design
Base path: `/api`
Auth: secure cookie session after login
Format: JSON unless otherwise noted

## 1. API principles

- REST API for MVP.
- Mutating endpoints require authentication and CSRF protection.
- All admin/operational endpoints require authentication.
- Errors use consistent problem details shape.
- Long-running operations enqueue Hangfire jobs and return job/run IDs.
- Search endpoints return explainable ranking components.

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

### 2.3 Accepted job

```json
{
  "jobId": "hangfire-job-id",
  "ingestionRunId": "uuid",
  "statusUrl": "/api/ingestion/runs/{id}"
}
```

## 3. Auth endpoints

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

### POST `/api/auth/change-password`

Request:

```json
{
  "currentPassword": "old",
  "newPassword": "new"
}
```

## 4. Settings endpoints

### GET `/api/settings`

Returns user/admin configurable settings.

### PUT `/api/settings`

Request example:

```json
{
  "ingestion.defaultMaxAgeDays": 30,
  "ingestion.defaultConcurrency": 2,
  "ingestion.maxSegmentsPerVideo": 60,
  "screenshots.offsetSeconds": 5,
  "search.textWeight": 0.5,
  "search.vectorWeight": 0.5,
  "notifications.matrix.onManualRuns": true,
  "notifications.matrix.onScheduledRuns": true
}
```

## 5. Channel endpoints

### GET `/api/channels`

Query params:

- `includePaused` boolean
- `page`
- `pageSize`

Response item:

```json
{
  "id": "uuid",
  "youtubeChannelId": "UC...",
  "name": "Channel Name",
  "profileUrl": "https://www.youtube.com/channel/UC...",
  "isPaused": false,
  "lastIngestedAt": "2026-07-16T00:00:00Z",
  "lastIngestionStatus": "processed_with_warnings"
}
```

### POST `/api/channels`

Request:

```json
{
  "sourceUrl": "https://www.youtube.com/@example",
  "defaultMaxAgeDays": 30,
  "defaultBackfillMaxVideos": 100
}
```

Response: created channel.

### GET `/api/channels/{channelId}`

Returns channel details and recent ingestion status.

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

### DELETE `/api/channels/{channelId}`

Query params:

- `deleteRelatedData` boolean, default false
- `confirm` string, required when deleting related data

Behavior:

- If `deleteRelatedData=false`, stop future ingestion/remove channel config according to implementation policy.
- If `true`, delete related videos/data/screenshots where safe.

## 6. Ingestion endpoints

### POST `/api/ingestion/run`

Start manual ingestion for all active channels.

Request:

```json
{
  "maxAgeDays": 30,
  "notifyOnCompletion": true
}
```

Response: accepted job.

### POST `/api/channels/{channelId}/ingestion/run`

Start manual ingestion for one channel.

Request:

```json
{
  "maxAgeDays": 30,
  "notifyOnCompletion": true
}
```

### POST `/api/channels/{channelId}/ingestion/backfill`

Request:

```json
{
  "backfillDays": 365,
  "maxVideos": 500,
  "notifyOnCompletion": true
}
```

Backfill uses its own days/max-count parameters and can exceed the default max-age lookback.

### GET `/api/ingestion/runs`

Query params:

- `status`
- `runType`
- `from`
- `to`
- `page`
- `pageSize`

### GET `/api/ingestion/runs/{runId}`

Response includes summary counts and item status.

### GET `/api/ingestion/runs/{runId}/items`

Query params:

- `status`
- `stage`
- `itemType`

### POST `/api/ingestion/items/{itemId}/retry`

Retries failed video/link/repo item.

### POST `/api/videos/{videoId}/retry`

Retries full video ingestion.

### POST `/api/external-links/{linkId}/retry`

Retries link scraping/classification/repository processing.

### POST `/api/repositories/{repositoryId}/retry`

Retries repository metadata/README/LICENSE/DeepWiki processing for GitHub repositories in MVP. GitLab and Bitbucket are MVP+.

## 7. Search endpoints

### POST `/api/search`

Request:

```json
{
  "query": "code project that searches for project ideas not yet achieved across all of github",
  "filters": {
    "channelIds": ["uuid"],
    "publishedFrom": "2026-01-01T00:00:00Z",
    "publishedTo": "2026-12-31T23:59:59Z",
    "resultTypes": ["video_cluster", "segment", "repository", "external_link", "note"],
    "linkClassifications": ["code_repository", "website_resource"],
    "hasTranscript": true,
    "hasRepo": true,
    "hasNotes": false,
    "ingestionStatuses": ["processed", "processed_with_warnings"]
  },
  "ranking": {
    "textWeight": 0.5,
    "vectorWeight": 0.5
  },
  "page": 1,
  "pageSize": 25
}
```

Response item is a video cluster search result. One video appears at most once per response page.

```json
{
  "id": "video-cluster-result-uuid",
  "resultType": "video_cluster",
  "sourceEntityType": "video",
  "sourceEntityId": "uuid",
  "title": "Video title or override",
  "snippet": "Primary matched snippet...",
  "score": 0.87,
  "relativeSimilarityPercent": 87,
  "scoreExplanation": {
    "textScore": 0.73,
    "vectorScore": 0.91,
    "textWeight": 0.5,
    "vectorWeight": 0.5,
    "matchedFields": ["transcript_chunk", "segment_title"],
    "reason": "Semantic match in transcript at 12:34 and partial text match in segment title."
  },
  "video": {
    "id": "uuid",
    "youtubeVideoId": "abc123",
    "title": "Video title",
    "author": "Author",
    "publishedAt": "2026-07-16T00:00:00Z",
    "watchUrl": "https://www.youtube.com/watch?v=abc123&t=754s",
    "channelUrl": "https://www.youtube.com/channel/UC..."
  },
  "segment": {
    "id": "uuid",
    "startSeconds": 754,
    "endSeconds": 910,
    "screenshotUrl": "/api/screenshots/{id}"
  },
  "links": [
    {
      "type": "repository",
      "url": "https://github.com/owner/repo",
      "title": "owner/repo"
    }
  ],
  "relatedItems": [
    {
      "type": "video_cluster",
      "id": "uuid",
      "title": "Related item",
      "relativeSimilarityPercent": 81
    }
  ],
  "warnings": ["transcript_missing"],
  "hasNotes": true
}
```

### GET `/api/search/suggestions`

Optional query suggestions/facets.

Query params:

- `q`
- `limit`

## 8. Video endpoints

### GET `/api/videos/{videoId}`

Returns video metadata, segments, links, repositories, transcript status, and notes summary.

### GET `/api/videos/{videoId}/segments`

Returns segment list with screenshots.

### GET `/api/videos/{videoId}/transcript`

Returns transcript cues/chunks.

### GET `/api/videos/{videoId}/links`

Returns extracted links and processing status.

### DELETE `/api/videos/{videoId}`

Query params:

- `deleteScreenshots` boolean, default true
- `confirm` string

## 9. Edit/override endpoints

### PUT `/api/videos/{videoId}/overrides`

Request:

```json
{
  "author": "Override author",
  "title": "Override title",
  "description": "Override description"
}
```

Response includes effective values and stale embedding status.

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

### PUT `/api/external-links/{linkId}/overrides`

Request:

```json
{
  "title": "Override title",
  "description": "Override description",
  "classification": "website_resource"
}
```

If classification changes, create `classification_corrections` entry.

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

## 10. Notes endpoints

### GET `/api/notes`

Query params:

- `targetType`
- `targetId`

### POST `/api/notes`

Request:

```json
{
  "targetType": "segment",
  "targetId": "uuid",
  "title": "Idea",
  "markdown": "Use this for..."
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

## 11. Embedding endpoints

### POST `/api/embeddings/regenerate`

Regenerate all embeddings, typically after model change.

Request:

```json
{
  "model": "bge-m3",
  "onlyStale": false
}
```

Response: accepted job.

### POST `/api/embeddings/regenerate-item`

Request:

```json
{
  "sourceEntityType": "segment",
  "sourceEntityId": "uuid"
}
```

Response: accepted job.

### GET `/api/embeddings/status`

Returns model, stale count, failed count, and last regeneration time.

## 12. Screenshot/media endpoints

### GET `/api/screenshots/{screenshotId}`

Returns WebP file.

### DELETE `/api/videos/{videoId}/screenshots`

Purge screenshots for video.

### DELETE `/api/channels/{channelId}/screenshots`

Purge screenshots for channel.

## 13. Repository endpoints

### GET `/api/repositories/{repositoryId}`

Returns metadata, README status, LICENSE status, DeepWiki URL, source links/videos.

### GET `/api/repositories/{repositoryId}/documents`

Returns README/LICENSE documents.

### POST `/api/repositories/{repositoryId}/check-deepwiki`

Queues DeepWiki check.

## 14. External link endpoints

### GET `/api/external-links/{linkId}`

Returns link details, classification, scrape status, repo association if any.

### POST `/api/external-links/{linkId}/reclassify`

Queues local LLM reclassification using current correction history.

### POST `/api/external-links/{linkId}/rescrape`

Queues Crawlee/Playwright rescrape.

## 15. Admin health/test endpoints

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

Sends Matrix test notification. E2EE/encrypted sends are MVP+.

### POST `/api/admin/test-embedding`

Request:

```json
{
  "text": "test embedding"
}
```

Returns model/dimensions and latency.

### POST `/api/admin/test-audio-to-text`

Runs service health test; may use bundled tiny audio fixture.

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

## 16. Matrix notifier internal API

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

## 17. Scraper internal API

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
  "rawHtmlDebugPath": null
}
```

## 18. Audio-to-text internal API

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

## 19. Versioning

MVP can use unversioned `/api` paths. Add `/api/v1` before external/public API commitments if the app grows beyond personal use.
