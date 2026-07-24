### Task 5.5: Implement unavailable-video terminal state

Source: `docs/architecture/DATA_MODEL.md` §3.6 (`unavailable`)

Requirements:

- When the platform definitively reports a video deleted or private during metadata fetch, transition `ingestion_status` to `unavailable` (terminal).
- Stop metadata retries for unavailable videos; watch links become best-effort.
- Preserve all stored artifacts (transcripts, segments, screenshots, search documents, embeddings).
- Scheduled runs skip unavailable videos without counting them as failures; unavailable videos remain searchable with warning state.

Verification:

- Fixture: metadata fetch reports a deleted video → status becomes `unavailable`, no further retries are scheduled, and stored artifacts are preserved.
- Scheduled run skips unavailable videos without failing the run.

## Phase 6: Transcript and audio-to-text

