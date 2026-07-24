### Task 15.5: Implement retention and cleanup jobs

Requirements:

- Enforce telemetry retention selected during first run: 90 days when free space is > 5 GB, 30 days when > 1 GB, otherwise disabled with warning.
- Clean up detailed domain/ingestion events according to configured retention while preserving long-lived ingestion run summaries.
- Purge screenshots and raw HTML debug captures from mounted volumes when corresponding records are purged/deleted.
- Preserve raw transcripts and screenshots indefinitely unless explicitly purged/deleted.

Verification:

- Retention job deletes expired detailed events but not retained run summaries.
- Channel/video delete with media purge removes screenshot/debug files from disk.
- Low-disk first-run fixture disables or lowers telemetry retention with a visible warning.

## Phase 16: Admin operations

