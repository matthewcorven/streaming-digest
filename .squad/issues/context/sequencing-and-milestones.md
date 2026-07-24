## Implementation sequencing

Execution order is the vertical slices below; phase numbering is a reference grouping for requirements, not the build order. Each slice produces a testable increment. Prototypes run as early as possible (ideally first, per user directive) so their findings — and any costly pivots — land before the implementation they inform, not after.

1. Foundation: solution, config, fixtures, baseline observability (Phase 0), database foundation and settings seeding (Phase 1).
2. Prototypes first: Task 7.4 (screenshot extraction, needs only the Task 0.4 fixture), Task 11.3a (vector knowledge base, needs only Postgres + pgvector from slice 1), Task 11.3b (vector user search, needs 11.3a's corpus) — minimal-dependency order. The 11.3a corpus generator becomes the seed of the Task 12.8 dataset generator; 11.3b's ranking findings feed Task 12.3. ADRs land where outcomes change decisions.
3. Auth + channel CRUD + Hangfire (Phases 2-4, including the Task 2.3b conformance harness, the Task 2.6 security conformance harness, and the Task 4.6 concurrency harness).
4. Basic yt-dlp metadata ingestion (Phase 5).
5. Transcript ingestion + search documents + embeddings + basic search UI (Phases 6, 11, early 12) - first end-to-end killer-journey checkpoint: a vague query returns a video cluster. Tasks 11.3 and 12.3 revalidate against the prototype findings from slice 2 rather than starting from untested assumptions.
5. Segmentation + screenshots (Phase 7).
6. Link/repo ingestion (Phases 8-9).
7. Website scraping, including scraper toolchain wiring (Phase 10).
8. Local LLM classification/semantic segmentation (8.4, 7.3).
9. Whisper fallback (6.3-6.4).
10. Notes/edit/re-embedding (Phase 13).
11. Video-cluster aggregate embeddings, high-signal matching, daily digest dashboard, recall harness, and performance measurement (11.7, 12.5-12.8).
12. Matrix notification audit/outbox; E2EE is MVP+ (Phase 14).
13. Production observability stack, retention, deployment, backup/restore, and upgrade hardening (15.2-15.5, 16.1, 16.3, 17). Task 16.2 contextual actions land with their owning slices per the placement map.
14. REST API contract conformance and end-to-end acceptance tests (Phases 18-19).

Even though all are hard MVP, this sequence produces testable increments and validates the killer journey as early as slice 5.

### Convergence milestones

Mid-plan checkpoints that validate multiple converging workstreams together, so integration failures surface before the end-to-end phase. Each milestone names its required slices and a pass/fail scenario.

**M1 — Killer journey smoke (after slice 5).** Requires slices 1–5 green.
Given a configured channel with one captioned long-form fixture video, when a manual ingestion run completes and the user submits a vague natural-language query, then the video appears as one cluster in the top results with metadata and a transcript match, and the search page is reachable post-onboarding.

**M2 — Enriched video cluster (after slice 9).** Requires slices 6–9 green on top of M1.
Given one fixture video whose description links a GitHub repository and a non-ad website, when the full pipeline runs, then a single search result cluster surfaces the repository, the scraped website, segment timestamps, and screenshot thumbnails together, with no duplicate clusters for the video.

**M3 — Digest and signal pipeline (after slice 12).** Requires slices 10–12 green on top of M2.
Given a completed ingestion run and a stored recent search, when the digest dashboard renders, then new videos/resources, high-signal matches with absolute-similarity percentages, and the pending-action inbox appear in the required priority order, and the recall harness gate passes on the ~500-video corpus.

**M4 — Notification and transition parity (after slice 14).** Requires slices 13–14 green on top of M3.
Given a completed run with a stored Digest, when the Matrix notification is sent, then the notification is an excerpt of the same stored Digest as the dashboard (ADR-0006); and given an embedding-model change, when the transition completes, then the scheduled-run pause, the single catch-up run, and high-signal backfill behave per ADR-0008/ADR-0011.

Phase 19 end-to-end scenarios remain the final acceptance gate; milestones M1–M4 exist to catch cross-workstream regressions earlier.

## Verification evidence

Verification that produces durable results — benchmarks, recall reports, cross-platform checks, restore dry-runs, prototype comparisons — must commit the evidence, not just the method. Convention:

- Committed under `docs/verification/` named `{task-id}-{slug}.md` (prose summary, environment, date, outcome), with machine-readable JSON alongside where the result is numeric (recall, latency).
- Evidence is append-only: re-runs add new dated files rather than overwriting, so regressions are visible across runs.
- Quality gates that cite measured targets (Task 12.7 recall, Task 12.8 latency) are not met until the corresponding evidence artifact is committed.
