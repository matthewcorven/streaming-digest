# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest quality work spans API behavior, ingestion resilience, search relevance, and UI regression risk.

## Recent Updates

📌 2026-07-24 (via Morpheus, user-approved plan edit): New **Task 2.6** — security conformance test harness, the third conformance harness in your lane alongside Task 2.3b (API contract) and Task 4.6 (concurrency). Covers Hangfire dashboard (`/admin/jobs`) auth rejection, screenshot endpoint (`/api/screenshots/{id}`) auth, path-traversal rejection on media serving, internal endpoints (`/internal/matrix/*`, `/internal/scrape/*`, `/internal/audio-to-text/*`) unreachable publicly, diagnostics (e.g. `GET /api/config/runtime`) never leaking secrets, and CSRF rejection of token-less mutations beyond login (extends Task 2.2). Harness runs in CI from Phase 2; later-phase scenarios land alongside their owning phases. Sequencing slice 2 updated. This was the final parked item from the plan review series — the review is now fully closed.
📌 Team initialized on 2026-07-18
📌 2026-07-24 (via Morpheus, user-approved plan resolutions — depth/evidence pass): Two significant additions in your lane — **Task 4.6** is a new concurrency/race conformance harness: failing-before/passing-after tests for race-prone invariants (parallel retries, single-active-generation unique index, one-active-transcript under concurrent cutovers, near-simultaneous digest assembly, outbox double-dispatch). Harness plus retry/unique-index scenarios land in Phase 4; later-feature scenarios land with their features. Sequencing slice 2 now cites it. **New `## Verification evidence` convention**: durable verification results (benchmarks, recall reports, cross-platform checks, restore dry-runs, prototype comparisons) are committed under `docs/verification/` as `{task-id}-{slug}.md` (prose + environment + date + outcome) with machine-readable JSON alongside for numeric results — append-only, re-runs add new dated files so regressions are visible. Quality gates citing measured targets (12.7 recall, 12.8 latency) are BLOCKED until the evidence artifact is committed — do not sign off on those gates without the file. Evidence bullets added to Tasks 12.7, 12.8, 17.1, 17.4, and the Quality gates list. Also: Task 15.1 now requires automated OTLP smoke assertions (integration test asserting trace spans + structured log fields incl. correlation ID through the OTLP pipeline), and new Task 1.5 adds a domain event type catalog with a convention test that fails on uncatalogued `event_type` values.
📌 2026-07-20 (via Morpheus, user-approved plan resolutions): New tasks with verification duties for you — **5.5** (unavailable terminal ingestion state lifecycle), **7.3a** (segment regeneration cutover), **12.5a** (run-scoped Digest assembly/storage), **17.5a** (Upgrade & Maintenance admin panel). Task 12.7 note: the recall harness stays hard-MVP, but MVP dataset growth is file-based (edit the golden dataset directly) — the in-UI capture/review queue is MVP+, so don't test for it in MVP. Task 16.2's contextual admin actions were distributed to owning slices via a placement map; sequencing slice 13 now cites 16.1/16.3 only.
📌 2026-07-19 (via Morpheus, user-approved plan edits): `docs/implementation/IMPLEMENTATION_PLAN.md` now defines convergence milestones M1–M4 (Given/When/Then pass/fail scenarios in `## Implementation sequencing`, gated on cumulative slices 4, 8, 11, 13). These milestones gate your test slices — M1–M4 must pass at their declared slices per the Quality gates list. Plan test work against them.
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

## Learnings

Switch is the primary tester and reviewer gate for the squad.

## 2026-07-28 — Independent re-review: PR #180 final verdict

Completed final independent re-review of PR #180 ([Task 11.4] Store embeddings in pgvector), revision commit 4b34e19 (Morpheus). All prior blockers verified resolved: production path into transcript ingestion + embedding persistence; host wiring for whisper fallback/media resolver; API host wiring for IStreamingDigestDbContext mapping; regression coverage alignment with shipped DI wiring. **Verdict: ready for review.** Ralph is in stopped state; this is a one-off sign-off entry, not a resumed queue loop.

## 2026-07-30 — Completed issue #31 search recall harness

Completed implementation of issue #31 ([Task 12.7] Search recall harness) on branch `matthewcorven-search-recall-harness` (commit 8812621). Issue #31 closed on GitHub as resolved.

Key implementation highlights:
- 500-video recall harness with 21-query golden dataset
- Deterministic distractor corpus builder
- Verification evidence committed to docs/verification/12.7-search-recall-harness.md and .json
- Unit regression and snapshot tests added
- Authenticated API integration test asserting each expected cluster stays in top 3

Session: 06b75168-0971-4ee2-9346-108721347684

## 2026-08-03 — Independent adversarial review: PR #228 (issue #199, Model WS-1)

Completed a second independent adversarial review of PR #228 (`IModelRuntimeClient` + `OllamaModelRuntimeClient`), head aecce5d. Formed from PR diff + Ollama API contract only (no other reviewer's artifacts). Reproduced: build clean, 433/433 unit tests pass. **Verdict: needs-changes (minor/additive)** — NDJSON partial-chunk resilience test missing (G1), singleton-pinned HttpClient DNS-staleness + 100s-timeout risks for streamed pulls (G2/G3, fix-or-explicit-defer), plus recommended fixture/coverage symmetry items (G4/G5) and a test-name typo (G6). Completeness after prescribed fixes: 100%. Artifacts in session 12a3ff05 files dir.

Session: 12a3ff05-8e63-40fe-a6c3-aeccec094e07

### Post-review reconciliation (2026-08-03)
Morpheus challenged my show-families claim with evidence from a stale diff snapshot (pre-fix b9105b0). Re-verified at head aecce5d: his prescribed fix was already applied (details-bound families, wire-shaped fixtures); he withdrew the challenge. Both reviews now target the same revision. Lesson: pin review claims to head SHA and re-pull before cross-reviewer disputes. Tank is applying my G1–G6 spec; I'll flip to approve on re-run once G1–G3 + G6 land.
