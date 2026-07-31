# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17

## Core Context

Streaming Digest is a self-hosted YouTube knowledge ingestion and search system.
Primary stack: ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Aspire orchestration, Docker Compose, and local AI services.
Requested by Matthew Corven.

## Recent Updates

📌 Team initialized on 2026-07-17
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Scribe duties: preserve ADR pointer lines VERBATIM on inbox merge; `git add docs/adr/` alongside `.squad/` when committing. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

## Learnings

Initial setup complete.
The squad uses The Matrix cast for active agent names.

## 2026-07-28 — Scribe sign-off: PR #179 re-review verdict recorded

Recorded Morpheus re-review verdict for PR #179 (Tank revision commit 80c983e): **ready for review.** Key finding: upgrade-path migration safety for legacy materialized view shape confirmed safe. Updated orchestration log and propagated status to Morpheus, Tank, and Ralph agent histories. Issue #25 advanced to ready-for-review; Issue #17 closed as stale-open via prior PR #178 merge.

## 2026-07-28 — Scribe sign-off: PR #181 re-review verdict recorded

Recorded Morpheus re-review verdict for PR #181 (Dozer revision commit 19b67a8ab321c2f259390a3b70cfc7d40d9a66af): **ready for review.** Key finding: ADR-0012 threshold calibration validated against real embedding provider; fresh installs seed 70; upgrades preserve existing stored values unless deliberately changed. Updated orchestration log and propagated status to Morpheus, Dozer, and Ralph agent histories. Issue #100 advanced to ready-for-review; PR #180 remains under active revision and not yet ready.

## 2026-07-28 — Scribe sign-off: PR #180 final independent re-review verdict recorded

Recorded independent re-review verdict for PR #180 (Morpheus revision commit 4b34e19): **ready for review.** Independent reviewer: Switch. All prior blockers resolved: production path into transcript ingestion + embedding persistence, host wiring for whisper fallback/media resolver, API host wiring for IStreamingDigestDbContext mapping, regression coverage alignment with shipped DI wiring. Ralph is stopped; this is a one-off sign-off log entry. Orchestration log entry created. Status advanced to ready-for-review.

## 2026-07-28 — Scribe sign-off: PR #179 merged and Issue #25 closed

Recorded state update: PR #179 ([Task 12.2] Implement vector search SQL) now merged into main; Issue #25 closed as resolved. Ralph remains stopped. One-off state update orchestration log entry created.


## 2026-07-28 — State update: PR #180 merged and Issue #20 closed

Recorded state update for merged PR #180 ([Task 11.4] Store embeddings in pgvector) and closed Issue #20. Ralph remains stopped. Appended orchestration.log entry and updated Ralph history. This completes the one-off state update requested by Matthew Corven.


## 2026-07-28 — Scribe sign-off: PR #181 merged and Issue #100 closed

Recorded state update: PR #181 ([Task 12.x] Calibrate ADR-0012 high-signal absolute-cosine threshold against the real embedding provider) now merged into main; Issue #100 closed as resolved. Ralph remains stopped. One-off state update orchestration log entry created. Threshold calibration validates against real embedding provider; fresh installs seed 70; upgrades preserve existing stored values.

## 2026-07-30 — Scribe sign-off: Neo completed issue #28 cluster ranking

Neo completed implementation of issue #28 ([Task 12.5] Implement cluster ranking and similarity percentages) on branch `matthewcorven-neo-issue-28-cluster-ranking` (commit 90426f7). Issue #28 closed on GitHub as resolved.

Key implementation highlights:
- Cluster ranking by relative similarity percentages
- Document hits grouped by video before ranking
- Override titles preferred when available
- Related items computed from whole corpus with relative-similarity percentages
- Unit and integration coverage included
- 70% high-signal threshold validated against docs/verification calibration data

Session: b38d616e-5497-4a35-8b02-a5924b35d24f

## 2026-07-30 — Scribe sign-off: Neo completed issue #22 recent-search storage

Neo completed implementation of issue #22 on branch `matthewcorven-issue-22-recent-search-storage` (commit 4f3e309). Issue #22 closed on GitHub as resolved.

Key implementation highlights:
- PostgreSQL-backed recent-search storage with full persistence layer
- Query embeddings computed and stored for ranking
- User interaction events recorded for opened search results  
- Search API and UI wired to clear history and record opens
- Interaction counts integrated into ranking boosts
- Migration 016_add_recent_search_history.sql added
- Focused unit and integration coverage for persistence, clear-all behavior, and interaction-driven ranking

Session: b06ad641-c9b7-4ab7-bfef-034c158d2688


## 2026-07-30 — Scribe sign-off: Neo completed issue #23 cluster aggregate embeddings

Neo completed implementation of issue #23 ([Task 12.6] Cluster aggregate embeddings) on branch `matthewcorven-video-cluster-aggregate-embeddings` (commit 283d8bf). Issue #23 completed.

Key implementation highlights:
- Brought in prerequisite issue #22/#28 work (commits 58403c3, 0f3e1a5)
- Added video_cluster_embeddings migration and PostgreSQL store
- Implemented aggregate-vector build, high-signal, and related-item query APIs
- Added cluster stale invalidation from search-document mutations
- Wired dependency injection
- Added focused PostgreSQL-backed tests for build, stale invalidation, and provider/model/dimension mismatch filtering
- Focused verification passed across SearchDocumentEmbeddingStoreTests, PostgresRecentSearchStoreTests, SearchUiServiceTests, PostgresVideoClusterEmbeddingStoreTests, PostgresMigrationBaselineTests, PostgresMigrationSupportTests, and DigestAssemblyServiceTests

Session: 250d2831-13f4-4151-bed9-078ca400939d

Branch: matthewcorven-video-cluster-aggregate-embeddings

Commit: 283d8bf

## 2026-07-30 — Scribe sign-off: Switch completed issue #31 search recall harness

Switch completed implementation of issue #31 ([Task 12.7] Search recall harness) on branch `matthewcorven-search-recall-harness` (commit 8812621). Issue #31 closed on GitHub as resolved.

Key implementation highlights:
- 500-video recall harness with 21-query golden dataset
- Deterministic distractor corpus builder
- Verification evidence committed to docs/verification/12.7-search-recall-harness.md and .json
- Unit regression and snapshot tests added
- Authenticated API integration test asserting each expected cluster stays in top 3

Session: 06b75168-0971-4ee2-9346-108721347684

Branch: matthewcorven-search-recall-harness

Commit: 8812621