### Task 5.3: Implement long-form and max-age filtering

Source: `docs/product/PRD.md` §2.2; `docs/architecture/DATA_MODEL.md` §3.2 (`ingestion.minDurationSeconds`)

Rules:

- Exclude Shorts per the Long-form selection rule: platform Shorts signal (`/shorts/` URL form or Shorts metadata flag) or duration below `ingestion.minDurationSeconds` (default 61). Excluded videos are counted in `videos_skipped` with reason `short_form` on the run — selection only, never retryable.
- Regular public long-form videos only.
- Default max age 30 days.
- Backfill uses separate days/max-count and preserves the idempotency guard (already-processed videos are skipped; Backfill is never an implicit Reprocess). Backfill produces a Digest marked `run_type: backfill`; Matrix notification defaults off via `notifications.matrix.onBackfillRuns`.

Verification:

- Unit tests for filtering.
- Backfill over previously processed videos skips them.

