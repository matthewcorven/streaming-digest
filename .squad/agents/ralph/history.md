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

Initial setup complete.
Ralph monitors issue, PR, and follow-up work for the configured squad roster.
