# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest quality work spans API behavior, ingestion resilience, search relevance, and UI regression risk.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-07-20 (via Morpheus, user-approved plan resolutions): New tasks with verification duties for you — **5.5** (unavailable terminal ingestion state lifecycle), **7.3a** (segment regeneration cutover), **12.5a** (run-scoped Digest assembly/storage), **17.5a** (Upgrade & Maintenance admin panel). Task 12.7 note: the recall harness stays hard-MVP, but MVP dataset growth is file-based (edit the golden dataset directly) — the in-UI capture/review queue is MVP+, so don't test for it in MVP. Task 16.2's contextual admin actions were distributed to owning slices via a placement map; sequencing slice 13 now cites 16.1/16.3 only.
📌 2026-07-19 (via Morpheus, user-approved plan edits): `docs/implementation/IMPLEMENTATION_PLAN.md` now defines convergence milestones M1–M4 (Given/When/Then pass/fail scenarios in `## Implementation sequencing`, gated on cumulative slices 4, 8, 11, 13). These milestones gate your test slices — M1–M4 must pass at their declared slices per the Quality gates list. Plan test work against them.
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

## Learnings

Switch is the primary tester and reviewer gate for the squad.
