### Task 4.3: Implement custom job progression and batch tracking

Use Hangfire OSS jobs plus application-owned progression/batch tracking in PostgreSQL. Do not depend on Hangfire Pro batches for MVP.

Retryable stage names:

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

Retry can operate at video, stage, external link occurrence, external resource, repository, search-document/embedding, and notification levels as needed. Vocabulary (ADR-0002): Retry applies to failed/deferred work only and is idempotent; Reprocess re-runs the full pipeline for any entity whose pipeline completed (any status other than Core-Stage failure, including `processed_with_warnings`), bypassing the idempotency guard, and re-evaluates scrape-exclusion policy against the live site (ADR-0014). Retry Budget (DATA_MODEL §3.29): 2 automatic backoff attempts + 5 manual Retries per item-stage; reaching the cap sets `is_retryable = false`, and Reprocess resets the budget.

Job payload durability per `docs/operations/UPGRADE_PATHS.md`:

- Use stable, versioned DTOs for all serialized Hangfire job payloads; record `job_payload_version` on ingestion items.
- Old serialized payloads map to current DTOs or are marked cancelled/retryable from the UI instead of failing deserialization.

Verification:

- Retry UI can select failed stages/items without Hangfire Pro.
- Old queued job/stage names can be mapped or cancelled/recreated safely.
- Fixture job enqueued with a prior payload version deserializes or is surfaced as retryable rather than crashing the worker.

