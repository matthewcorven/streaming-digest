### Task 12.5a: Implement digest assembly and storage

Source: `docs/architecture/DATA_MODEL.md` §3.37; ADR-0006; ADR-0012

Requirements:

- Assemble the run-scoped Digest once when an ingestion run completes; store `payload_json` with new videos, new resources (repositories, websites), high-signal matches, failed/skipped items, and active deferments.
- One assembly, two renderings: the dashboard (Task 12.6) and Matrix notification (Task 14.3) render from the stored artifact and never independently compute. Sole exception: the dashboard's active-deferments subsection re-derives from live state at render time (ADR-0006 amendment).
- High-signal evaluation runs once at assembly time against recent-search embeddings using raw cosine similarity against the global threshold (`search.highSignalThresholdPercent`, default 80) — an absolute scale, not the rank-relative `relativeSimilarityPercent` (ADR-0012).
- Runs completing during an Embedding Transition skip high-signal evaluation; the single catch-up run after the transition backfills evaluation for transition-era videos (ADR-0008, ADR-0011).
- Backfill runs produce a Digest marked `run_type: backfill`.

Verification:

- A completed run stores exactly one digest row containing all payload sections.
- Dashboard and Matrix renderings of the same run agree, except the live-deferments subsection.
- A run completing inside a transition window omits high-signal matches; the catch-up run backfills them.

