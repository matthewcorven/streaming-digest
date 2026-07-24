### Task 17.5a: Implement Upgrade & Maintenance admin panel

Source: `docs/operations/UPGRADE_PATHS.md`; `docs/api/API_SPEC.md` §18

Requirements:

- Admin UI Upgrade & Maintenance panel showing versions, upgrade status, backup status, migration preview, service compatibility, derived-data status, risk level, and post-upgrade checklist.

Verification:

- Upgrade & Maintenance panel renders risk level and required next action.

## Phase 18: REST API contract conformance

Requirements:

- Implement every MVP endpoint and response shape in `docs/api/API_SPEC.md`, including auth/CSRF, operations, ingestion, search, recent searches, video details, edit/override, notes, embeddings, screenshots, repositories, external resources/link occurrences, admin health/tests, backups, and maintenance endpoints.
- Endpoints whose behavior is explicitly MVP+ must be omitted from MVP docs or documented as MVP+ rather than implemented accidentally.
- Mutation endpoints return stale search-document IDs, stale cluster IDs, and queued operations where relevant.
- Errors use consistent RFC 7807-style problem details.
- Batch retry/reprocess/delete endpoints return per-item acceptance/rejection details.
- Includes `GET /api/search/suggestions` (API_SPEC §8) and `DELETE /api/videos/{videoId}` semantics (`deleteScreenshots` default true, `confirm` required) per `docs/api/API_SPEC.md` §8, §10.

Verification:

- API conformance test enumerates `docs/api/API_SPEC.md` MVP endpoints and verifies route existence/auth behavior; the Task 2.3b known-pending list is empty.
- Search response fixture matches video-cluster contract and excludes MVP+ link-classification filters.
- Admin health/test, maintenance, backup, screenshot, repository, and external-resource endpoint smoke tests pass.
- Channel/video deletion verifies shared-canonical-resource semantics: associations/occurrences are removed, and shared repositories/resources survive unless force-purged (Task 3.2).
- Old Hangfire payload fixture deserializes or is surfaced as retryable per Task 4.3.

## Phase 19: End-to-end acceptance tests

### Scenario 19.1: Captioned video ingestion

Given a configured channel with a recent long-form public video with captions:

- metadata stored.
- transcript stored.
- segments generated.
- screenshots generated.
- links extracted/classified.
- embeddings generated.
- search finds transcript segment.

### Scenario 19.2: No-caption video ingestion

Given a recent long-form public video without captions:

- temp audio/video downloaded.
- local transcription runs.
- temp files deleted.
- transcript stored.
- search finds transcript text.

### Scenario 19.3: Repository link

Given a video description includes a GitHub repo:

- repo stored.
- README stored and embedded.
- LICENSE stored.
- DeepWiki checked.
- result card links repo and parent video.

### Scenario 19.4: Website link

Given a video includes a non-ad website:

- first page scraped.
- visible text embedded.
- result card links website and parent video.

### Scenario 19.5: Edit and notes

- User edits transcript or title.
- Override history records previous value.
- Embedding regenerates using override.
- User adds EasyMDE note.
- Search finds note.

### Scenario 19.6: Matrix notification

- Manual ingestion completes.
- Dedicated bot sends summary to configured Matrix room; E2EE is MVP+.
- User sees message on Android Matrix client.

### Scenario 19.7: Observability

- API request trace visible.
- Worker ingestion trace visible.
- Logs in Loki.
- Metrics in Prometheus/Grafana.
- Domain event in Postgres.

### Scenario 19.8: Killer journey

Given a user adds one public YouTube channel and leaves the default scheduled run enabled:

- Scheduled ingestion runs.
- Search for a vague project idea returns the relevant video cluster in the top 3 results (asserted by the Task 12.7 recall harness).
- The cluster exposes top-level video metadata, warning state, and whatever timestamp/repository/website/note/related-item data is available at that time.
- Related items show visible `Relative similarity` percentages.
- Failures are prominent and retryable without reading logs.

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

## Quality gates

Before declaring MVP complete:

- `dotnet test` passes.
- Scraper service build/test commands pass (if Node/TypeScript per Task 10.1a).
- Integration tests run against PostgreSQL + pgvector.
- Compose stack starts cleanly.
- Local model health checks pass.
- Audio-to-text health check passes.
- Matrix test notification succeeds; encrypted test notification is MVP+.
- Search recall harness meets the top-3 target on the representative vague-query corpus (Task 12.7).
- Search latency meets P50/P95 targets on the ~500 and ~2,000 video representative datasets (Task 12.8).
- API contract conformance tests pass for all MVP endpoints and the Task 2.3b known-pending list is empty.
- Ingestion handles partial failures and retries.
- Backup/restore dry run documented and tested.
- Daily digest dashboard and pending-action inbox satisfy priority/order requirements.
- Notification audit/outbox retry behavior is verified.
- Video-cluster aggregate embeddings are generated, invalidated, and used for high-signal matching.
- Rate-limit deferments are persisted, enforced, surfaced, and clearable.
- Retention/cleanup jobs handle domain events, telemetry policy, screenshots, and raw debug captures.
- Convergence milestones M1–M4 pass at their declared slices.
- Durable verification evidence is committed per the Verification evidence convention (recall, latency, cross-platform, restore dry-run, prototype comparisons).

## Open implementation decisions

These are implementation-time choices, not product-scope blockers:

- Exact Matrix SDK/service technology.
- Exact whisper engine behind the HTTP audio-to-text service contract (whisper.cpp preferred); the service shape itself is decided (`docs/api/API_SPEC.md` §21).
- Exact Ollama LLM model default per hardware.
- HNSW vs IVFFlat pgvector index based on installed pgvector version and expected dataset size (informed by Task 11.3a prototype evidence).
- Whether Crawlee/Playwright runs in worker container or separate scraper container.
- Screenshot extraction approach (ffmpeg vs yt-dlp frame extraction) — resolved by the Task 7.4 prototype and recorded in an ADR.
- Ranking weight defaults — informed by Task 11.3b prototype evidence.
