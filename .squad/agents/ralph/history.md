# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17

## Core Context

Streaming Digest is a self-hosted YouTube knowledge ingestion and search system.
Primary stack: ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Aspire orchestration, Docker Compose, and local AI services.
Requested by Matthew Corven.

## Recent Updates

📌 Team initialized on 2026-07-17
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

## Learnings

Ralph monitors issue, PR, and follow-up work for the configured squad roster.

## 2026-07-27 — Ralph Round 1: "ralph, go" activation

Activated by Matthew Corven. Initial queue status:
- **Available for delegation:** #17 (Ollama hardening), #25 (pgvector vector search)
- **Blocked:** #20, #21, #22, #23, #26, #27, #28, #30, #31, #32, #100 (dependency chain)

Neo completed both available issues locally (hardening Ollama config aliases + endpoint normalization + clearer error handling + tests for #17; pgvector column integration + vector similarity search + regression test for #25), but GitHub issue state has not advanced yet—issue helper still reports both as available. Downstream work blocked pending upstream sync. Ralph round 1 in monitoring mode.

## 2026-07-28 — Issue #25 advanced to ready-for-review (via PR #179 re-review verdict)

Morpheus completed independent re-review of PR #179 (Tank revision commit 80c983e); upgrade-path migration safety for legacy materialized view shape confirmed safe. **Verdict: ready for review.** Issue #25 (pgvector vector search) now ready-for-review status per Ralph queue monitoring. Issue #17 (Ollama hardening) previously merged via PR #178, closed as stale-open.

## 2026-07-28 — PR #180 independent re-review verdict recorded: ready for review

Switch (independent reviewer) completed re-review of PR #180 ([Task 11.4] Store embeddings in pgvector), revision commit 4b34e19 (Morpheus). All prior blockers resolved. **Verdict: ready for review.** Status recorded in orchestration log. Ralph is stopped; this is a one-off sign-off entry.


## 2026-07-28 — State update: PR #180 merged, Issue #20 closed

PR #180 ([Task 11.4] Store embeddings in pgvector) merged into main. Issue #20 closed as resolved. Ralph remains in stopped state. Status recorded by Scribe.


## 2026-07-28 — Issue #100 resolved via PR #181 merge

PR #181 ([Task 12.x] Calibrate ADR-0012 high-signal absolute-cosine threshold against the real embedding provider) merged into main. Issue #100 closed as resolved. Ralph remains in stopped state. Status recorded by Scribe.
