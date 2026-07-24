# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest search quality depends on hybrid retrieval, embeddings, similarity signals, and enrichment quality.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-07-20 (via Morpheus, user-approved plan resolutions): New **Task 12.5a** owns run-scoped Digest assembly and storage (DATA_MODEL §3.37; ADR-0006, ADR-0012) — dashboard (12.6) and Matrix (14.3) render from the stored artifact. Task 12.5a also owns high-signal evaluation timing (ADR-0012 absolute similarity scale) during digest assembly. Also relevant: new Task 7.3a owns segment regeneration cutover (split from 7.3); Task 12.7 recall harness stays hard-MVP but its in-UI capture affordance is MVP+.
📌 2026-07-19 (via Morpheus, user-approved plan edits): `docs/implementation/IMPLEMENTATION_PLAN.md` now uses per-task `Source:` anchor lines (one line after each `### Task X.Y` heading, citing governing doc sections and ADRs). When planning or executing search/retrieval tasks, read the `Source:` anchors first — they are the authoritative traceability path to ARCHITECTURE.md, DATA_MODEL.md, API_SPEC.md, and ADRs.
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

📌 2026-07-24 (via Coordinator, user directive): New work in your lane — **Task 11.3a "Prototype vector knowledge-base approach"** and **Task 11.3b "Prototype vector user-search approach"** added to `docs/implementation/IMPLEMENTATION_PLAN.md` (slice 4, after 11.3). You own both when slice 4 starts. 11.3a: synthetic corpus generator + synthetic bulk embeddings with a real-embedding validation subset — validates document construction, staleness derivation, ADR-0004 per-video duplication, cluster aggregates, pgvector index trade-off. 11.3b: synthetic query generator — validates hybrid scoring, cluster aggregation, relativeSimilarityPercent, high-signal matching, related items, and explores ranking weight ranges; findings feed **Task 12.3** ranking weight defaults. The 11.3a corpus generator seeds the **Task 12.8** recall-harness dataset. Standing prototype policy (user directive 2026-07-24): synthetic programmatically generated data only — no AI-generated content, no latency/token cost, controlled content profile. Related ADRs (ADR-0004, ADR-0012) stay conditional on prototype outcome.

## Learnings

Neo owns ranking, vector search, embeddings, and search relevance decisions.
📌 2026-07-24 (via Coordinator, user directive — prototypes-first sequencing): Your prototype tasks **11.3a and 11.3b now run in slice 2 "Prototypes first"**, immediately after slice 1 foundation — no longer slice 4. They run before Tasks 11.3/12.3, which now explicitly revalidate against your slice-2 prototype findings. Task 11.3a's real-embedding validation subset is now optional-when-provider-exists so the prototype runs standalone without an embedding provider. Ranking weight findings still feed 12.3; corpus generator still seeds 12.8.
