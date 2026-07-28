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
