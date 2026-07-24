### Task 16.2: Implement contextual admin actions (UI/API)

Normal user actions should be provided contextually where they are useful (source: `docs/product/PRD.md` §2.6; `docs/api/API_SPEC.md` §7, §14, §17):

- run ingestion now.
- run channel backfill.
- retry failed video.
- retry failed link/repo.
- reprocess item (video/repo/resource — full pipeline, bypassing idempotency; embeddings regenerate as a consequence, ADR-0002).
- reprocess all embeddings after embedding-model change (the bulk model-change flow).
- purge screenshots for video/channel.
- test Matrix notification.
- test embedding service.
- test audio-to-text service.

Placement: each action surfaces with its owning slice rather than waiting for slice 13 — run/retry actions with slice 3 (Phase 4), link/repo retry and reprocess with slice 7 (Phases 8–9), screenshot purge with slice 6 (Phase 7), embedding test with slice 5 (Phase 11), audio-to-text test with slice 10 (Phase 6), bulk embedding reprocess with slice 12, and Matrix test with slice 13 (Phase 14).

Verification:

- Each action enqueues a job or returns a clear health result.

