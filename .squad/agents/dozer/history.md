# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest relies on Aspire orchestration and Docker Compose creation for local and deployment workflows.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-07-24 (via Morpheus, user-approved plan resolutions — depth/evidence pass): New work in your lane — **Task 7.4** is a screenshot toolchain prototype: ffmpeg frame extraction vs yt-dlp frame extraction against the test fixture, evaluated on quality/accuracy, size/encode cost, speed, toolchain footprint (macOS ARM, Windows ARM, Linux), temp-media fit, failure modes, and container complexity. The outcome is recorded in an ADR (next available number); the actual screenshot-generation task is renumbered to **7.5** and follows the ADR. **Task 6.4** is retitled "Implement temporary media lifecycle and transcription fallback" and now owns the shared temp-media lifecycle (quota, filename scheme, cleanup) for ALL pipeline stages — transcription, screenshot frame extraction (7.5), and anything future — design it stage-agnostic. **Task 10.1a** now includes a typed scraper client matching the internal scraper API contract (`docs/api/API_SPEC.md` §20), including health-check integration.
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

📌 2026-07-24 (via Coordinator, user directive): New **Task 11.3a "Prototype vector knowledge-base approach"** (slice 4, after 11.3) informs your lane — its pgvector index trade-off evaluation provides the evidence for the open **HNSW vs IVFFlat** index decision (previously unresolved in the plan's open implementation decisions; now marked "informed by Task 11.3a"). Prototype policy: synthetic programmatically generated data only — no AI, no token/latency cost, controlled content profile.

## Learnings

Dozer owns orchestration, environment wiring, and container topology.
