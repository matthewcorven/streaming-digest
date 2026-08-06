# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest is a self-hosted YouTube knowledge ingestion, search, curation, and observability platform.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-08-03: Ralph cycle complete — PRs #228, #229, and #230 merged after adversarial review, revision, and re-review.

## Learnings

Morpheus owns architecture, scope, and review across the squad.

## Older review summary (2026-07-18 → 2026-07-25)

- 2026-07-18: Reviewed Trinity's Fluent UI declaration in `ARCHITECTURE.md` (§2.1, §5.2); verdict consistent across `ARCHITECTURE.md`, `DATA_MODEL.md`, `IMPLEMENTATION_PLAN.md`, and `PRD.md`; flagged missing Fluent UI package mention in Task 0.2.
- 2026-07-18: Reviewed Trinity's PWA-first declaration in `ARCHITECTURE.md` (§1, §2.1, §5.2); approved as consistent with `PRD.md`, `IMPLEMENTATION_PLAN.md`, and `UPGRADE_PATHS.md`; flagged follow-ups for PWA asset tasks and service-worker upgrade handling.
- 2026-07-18: Reviewed `docs/presentation/PRESENTATION.md` against `PRD`, `ARCHITECTURE`, `API_SPEC`, `DATA_MODEL`, and `IMPLEMENTATION_PLAN`; verdict APPROVED WITH NOTES; applied the §3.3 post-login routing precedence fix.
- 2026-07-18: Amended `.github/agents/squad.agent.md` governance so architectural decisions land in `docs/adr/NNNN-<slug>.md` and `.squad/decisions.md` remains the single index.
- 2026-07-19: Reviewed the `docs/implementation/IMPLEMENTATION_PLAN.md` edit pass that added per-task `Source:` anchors, convergence milestones M1–M4, explicit Phase 16 tasks, and corrected Phase 2 ordering.
- 2026-07-19: Recorded ownership learnings: Task 12.5a digest assembly, Task 5.5 unavailable-video lifecycle, split cutover tasks (7.3/7.3a, 17.5/17.5a), and contextual admin actions via placement map.
- 2026-07-24: Reviewed the second implementation-plan edit pass: `docs/verification/` evidence convention, prototype-then-ADR pattern, Task 6.4 temp media ownership, Task 1.5 domain-event catalog, whisper HTTP-service posture, typed scraper client in 10.1a, and Task 4.6 concurrency harness.
- 2026-07-24: Recorded that security conformance is now named harness Task 2.6, covering dashboard auth, screenshot auth, path traversal, internal endpoint posture, secret non-leakage, and extended CSRF.
- 2026-07-25: Synthesized merged prototypes 11.3a/11.3b/7.4 into `docs/verification/prototype-synthesis-11.3a-11.3b-7.4.md` plus `spikes/README.md`; key learnings preserved: PROVEN-mechanism ≠ PROVEN-value, verification report ≠ decision record, latency denominator discipline, INCONCLUSIVE-by-construction, disproven framing as spike success, and spike code retained on main as evidence.
- 2026-07-25: Cross-agent update preserved: synthesis review PR #99 remained OPEN; requested coordinator follow-ups stayed recorded in `decisions.md` — `docs/adr/0016-vector-index-hnsw.md`, DATA_MODEL/ARCHITECTURE follow-ups, ADR-0012 calibration issue, and the real-model re-verification list on #17; Neo's 11.3a “no ADR” call remained overturned.

## 2026-07-28 — PR #179 re-review (independent) after Tank revision commit 80c983e

Re-reviewed Tank's revision of PR #179 ([Task 12.2] Implement vector search SQL) end-to-end. Key finding resolved: upgrade-path migration safety for legacy materialized view shape confirmed safe. **Verdict: ready for review.** PR #179 now ready for maintainer review; issue #25 advanced to ready-for-review in Ralph's queue.

## 2026-07-28 — PR #181 re-review (independent) after Dozer revision commit 19b67a8ab321c2f259390a3b70cfc7d40d9a66af

Re-reviewed Dozer's revision of PR #181 ([Task 12.x] Calibrate ADR-0012 high-signal absolute-cosine threshold against the real embedding provider) end-to-end. Key finding resolved: threshold calibration against the real embedding provider confirmed; default seed 70 verified for fresh installs; upgrade paths correctly preserve existing stored values unless deliberately changed. **Verdict: ready for review.** PR #181 now ready for maintainer review; issue #100 advanced to ready-for-review in Ralph's queue.

## 2026-07-28 — PR #180 final independent re-review after Morpheus revision commit 4b34e19

Independent reviewer Switch completed re-review of PR #180 ([Task 11.4] Store embeddings in pgvector) against final revision commit 4b34e19. All prior blockers resolved: production path into transcript ingestion + embedding persistence, host wiring for whisper fallback/media resolver, API host wiring for IStreamingDigestDbContext mapping, regression coverage alignment with shipped DI wiring. **Verdict: ready for review.** PR #180 now ready for maintainer review.

## 2026-08-02 — PR #228 final adversarial re-review (issue #199, WS-1 model runtime client) after Dozer fix commit fdc14d9

- Verdict: APPROVED. Morpheus completed four passes across commits `b9105b0` → `aecce5d` → `2f170f8` → `fdc14d9`; Switch also performed an independent second review; final blocker G2 closed by Dozer at `fdc14d9` after Tank's G1–G6 polish at `2f170f8`.
- Scope confirmed: `IModelRuntimeClient`, `OllamaModelRuntimeClient`, DI wiring, HTTP lifetime handling, and downstream model-runtime contract behavior.
- Final resolved blocker: singleton-held typed `HttpClient` / captured runtime configuration replaced with transient-per-operation usage via `IHttpClientFactory`, eliminating stale configuration and cross-lifetime coupling.
- Artifacts: merged PR review comments and session artifacts recorded under session `files/` directories for PR #228; commits preserved: `b9105b0`, `aecce5d`, `2f170f8`, `fdc14d9`.

## 2026-08-02 — PR #229 adversarial review and re-review (issue #210, A5 whisper runtime)

- Verdict: APPROVED. Morpheus reviewed the whisper runtime wiring change set and re-reviewed the final revision before merge commit `2db731e` landed on `main`.
- Scope confirmed: whisper runtime HTTP-service posture, host wiring, runtime registration, and contract alignment with the local AI stack.
- Outcome: no remaining blockers after revision; PR #229 merged cleanly to `main` at `2db731e`.
- Artifacts: merged PR review comments and session artifacts recorded under session `files/` directories for PR #229.

## 2026-08-02 — PR #230 adversarial review and final approval (issue #212, A6 DB hybrid search)

- First-pass verdict at commit pre-fix state: NEEDS-CHANGES. Build clean; 406 unit / 72 integ / 1 skipped — claims verified.
- Blocking: `PostgresSearchCorpusSearcher.BuildSearchSql` built `doc_scores` from `text_matches LEFT JOIN vector_matches`, leaving vector-only documents unreachable despite UNION intent. Integration tests never exercised the vector leg because the fake store returned null embedding.
- Moderate: dimensionless vector column + dimensions-integer filter did not guarantee same-length vectors; mixed dimensions could raise `different vector dimensions` and 500 the search path. Required fix: `vector_dims(e.embedding)` predicate.
- Minor: `matched_fields` hardcoded `''`; `EmbeddingErrorSink` recorded but was never read (acceptable interim pending WS-7 guard).
- Confirmed good: genuine waiting state, [0,1] clamping, one-cluster-per-video, HNSW/IVFFlat deferral rationale, AuthFlow fixture override, and fixture corpus retained only for tests/recall.
- Final outcome: Neo revised PR #230 per Morpheus's spec; Morpheus re-reviewed and approved the final merged result at commit `8590eb3` on `feat/application-truth`.
- Artifacts: `session files/completeness_after_any_fixes.txt` (92), `files/review_change_specifications.md`.
