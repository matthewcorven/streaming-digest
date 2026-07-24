### Task 7.5: Generate WebP screenshots

Rules:

- One per segment.
- Default timestamp: start + 5 seconds.
- Configurable offset.
- Store file on mounted volume.
- Store metadata/path in DB.
- Extraction approach (ffmpeg vs yt-dlp) follows the ADR recorded by the Task 7.4 prototype.
- If segments or screenshot offset change by explicit user action, purge/recreate screenshots immediately.
- Screenshots are never load-bearing: the serving endpoint returns a stable placeholder instead of 404 when a file is missing; the placeholder is visually distinct from real screenshots and broken images (branded "pending/failed, retry available" treatment), result cards prefer the platform thumbnail or no image over a wall of placeholders, the missing file is recorded as a domain event, and the row becomes retryable (Enrichment Stage), with per-video rollup `unknown`/`pending`/`partial`/`succeeded`/`failed`.

Verification:

- Test video fixture generates WebP under expected path.
- Metadata row created.
- Offset or segment-change request purges/recreates screenshots.

## Phase 8: Link extraction/classification

