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

📌 2026-07-25 (Task 11.3b complete — query-side vector user-search prototype, issue #19): Built the query-side companion to 11.3a on branch `matthewcorven-prototype-11-3b-vector-user-search`, reusing 11.3a's corpus, deterministic embedder (seed=42, dim=384), and still-running pgvector container (config-identical AppHost, shared UserSecretsId). Seeded synthetic query generator (seed=7, 5 kinds × 40 = 200 queries, zero AI) with answer-side ground truth persisted to a prototype-only `prototype_video_truth` table (11.3a's corpus used non-deterministic `Guid.NewGuid()` video IDs, so truth must be DB-persisted to survive across processes). Ran against REAL Aspire postgres 17.10 + pgvector 0.8.5 — text side real ts_rank_cd(title A/body B)+pg_trgm, vector side real pgvector `<=>` over HNSW. **Verdicts:** relSimPct (min-max over PRE-pagination top-200 pool), related_items (top-5 same-topic centroids 0.89–0.94), hybrid_blend, cluster_aggregation, note_interaction_boost all PROVEN (mechanism/plumbing); **adr0012 and paraphrase_vague_semantic_recall INCONCLUSIVE** (synthetic-boundary: embedder jitters from raw query text → no semantic invariance; synthetic abs-cosines own 0.4801 < best-other 0.5931 → no threshold separates; defer ADR-0012 numeric VALUE to real-model calibration, keep the mechanism). **No ADR filed** (formula unchanged) — decision note at `.squad/decisions/inbox/neo-11-3b-vector-user-search.md`. **Recommended Task 12.3 defaults:** text/vector 0.65/0.35 (sweep 0.35/0.65→0.80/0.20); aggregation 0.65/0.25/0.10; note_boost 0.08; interaction_boost min(0.05,0.01×count); relSimPct = min-max over pre-pagination pool. **Headline production finding:** hybrid latency ~447 ms/query vs 11.3a's 0.522 ms pure-vector — cost is per-query tsvector construction + ts_rank_cd + GROUP BY over 12k docs + per-video in-memory aggregation → Task 12.3 needs MATERIALIZED tsvector columns / pre-aggregation, not per-query tsvector construction. **Npgsql+pgvector quirks:** read `vector` cols via `NpgsqlConnection.GlobalTypeMapper.UseVector()` + `GetFieldValue<Pgvector.Vector>`; the `<=>` operator needs typed `new Pgvector.Vector(qVec)` — bare `float[]` binds as `real[]` → SQLSTATE 42883. **Shell gotcha:** `dotnet run | tail` hides progress till buffer flush — redirect to a log file and tail the file; `timeout` doesn't exist on macOS. Evidence: `docs/verification/11.3b-vector-user-search.md` + `.json`. One re-run command: `ConnectionStrings__streamingdigest="Host=localhost;Port=49760;..." dotnet run --project spikes/StreamingDigest.VectorPrototype -- query`. Prototype is throwaway — do not merge to main.

## 2026-07-25 — Task 11.3a vector knowledge-base prototype (Neo)

Ran the 11.3a throwaway prototype end-to-end against the **real Aspire-managed Postgres + pgvector** stack (no mocks). All five claims under test **PROVEN**. Branch `matthewcorven-prototype-11-3a-vector-knowledge-base`; report at `docs/verification/11.3a-vector-knowledge-base.md` (+ `.json`).

**Per-item verdicts:** document construction PROVEN · staleness derivation PROVEN · ADR-0004 cardinality (3 docs / 3 parents / 1 embedding) PROVEN · cluster centroid PROVEN · pgvector index behavior PROVEN.

**Key numbers** (500 videos, 11,958 embeddings, dim=384, pgvector 0.8.5 / PG 17.10): HNSW build 979ms, query 0.522ms, recall@10 0.99, 23.9MB · IVFFlat(lists=100,probes=10) build 422ms, query 0.509ms, recall 0.98, 19.6MB · exact seq-scan 3.22ms. **Recommendation: HNSW as MVP default** (incremental build, no tuning, recall headroom) — refines DATA_MODEL §3.22, does not overturn it. No new ADR.

**Learnings that matter for 11.3 / 12.x:**
- The AppHost **needs a pgvector image** (stock `postgres` has no extension). Pin `.WithImage("pgvector/pgvector","pg17").WithImageRegistry("docker.io")`. This is **production-needed**, promoted via normal PR (prototype branch is not merged).
- **Data-volume gotcha:** the named volume `streamingdigest-postgres-data` persists across image changes; if initialized by stock `postgres:18.x`, `pg17` fails `initdb` ("directory exists but is not empty"). `docker volume rm streamingdigest-postgres-data` before first pgvector run.
- **Aspire CLI 13.2.2 vs pinned SDK 13.4.6** is a non-issue (`dotnet run` uses the pinned SDK).
- **Staleness derivation is expressible in pure SQL** — Postgres `sha256(convert_to(...))` matches .NET SHA256 hex byte-for-byte (0 mismatches), validating the ADR-0001 "computed view/function" ergonomic.
- **ADR-0004 dedup is real but small at MVP scale** (1.34% embedding savings); at production volume with a paid provider it directly cuts provider calls/cost.
- **Npgsql gotchas:** use `NpgsqlBatch` for bulk inserts (PrepareAsync type inference fails on unset params); a C# `float[]` query parameter against a `vector` column needs an explicit `$1::vector` cast.
- **Boundary:** synthetic recall numbers are internal-consistency checks only; they do NOT transfer to a real embedding model. Real semantic recall must be re-measured in 11.3.

## 2026-07-25 — Cross-agent update (via Scribe): prototype series outcomes + Morpheus rulings affecting you

**Morpheus OVERTURNED your 11.3a "no ADR" call.** The HNSW-as-MVP-default recommendation meets the ADR bar (DATA_MODEL §3.22 explicitly left the choice open; hard-to-reverse migration; real measured trade-off). **ADR-0016 ("MVP vector index is HNSW, not IVFFlat") is now required** — coordinator owes `docs/adr/0016-vector-index-hnsw.md`. Lesson: when a prototype picks a previously-open option in a governing doc, treat that as an architectural decision needing an ADR, not just a refinement note. Your 11.3b "no ADR" call was CONFIRMED (no §6 formula change; latency finding is an implementation note).

**Other rulings affecting your lane:** (1) 447 ms hybrid latency → Task 12.3 hard-requirement: materialized tsvector generated column + GIN, not per-query tsvector; Task 12.8 owns re-measurement vs ≤2 s P50 @ 500 videos. (2) ADR-0012 threshold calibration gets a NEW issue (Depends On: 11.3): measure own-vs-best-other cosine gap with the real provider, sweep 0.70–0.98, set `search.highSignalThresholdPercent`; gate before Task 12.5a digest rendering is trusted. (3) Real-model evidence you owe: recall@k/MRR (12.7), cluster weight differentiation + cross-topic separation (12.7/12.3), HNSW at 2k videos (12.8). (4) Spike code stays on main as primary-source evidence under the new `spikes/README.md` convention (throwaway, excluded from slnx, evidence-linked, never imported by production). Synthesis doc: `docs/verification/prototype-synthesis-11.3a-11.3b-7.4.md` (PR #99, open).

## 2026-07-27 — Issues #17 and #25: Ollama hardening + pgvector vector search (local completion)

**Issue #17 (Ollama embedding provider hardening):** Resolved config/env alias handling with clearer failures on missing endpoints. Enhanced endpoint normalization. Added unit tests validating alias routes and error paths. Branch: local; tests passing.

**Issue #25 (pgvector-backed embedding_vector column):** Implemented pgvector column on embedding_vector, integrated vector similarity search into search_videos query path. Added regression test validating vector similarity scoring. Branch: local; tests passing.

**Blocker:** GitHub issue state has not advanced—both issues still reported as available by helper. Downstream issues (#20–#23, #26–#28, #30–#32, #100) remain blocked by dependency metadata. Awaiting upstream sync to unblock queue.

## 2026-07-30 — Issue #22 recent-search storage completed (Neo)

Completed implementation of issue #22 on branch `matthewcorven-issue-22-recent-search-storage` (commit 4f3e309). Issue #22 closed on GitHub as resolved.

**Implementation Summary:**
- PostgreSQL-backed recent-search storage with full persistence layer
- Query embeddings computed and stored for ranking
- User interaction events recorded for opened search results
- Search API and UI wired to clear history and record opens
- Interaction counts integrated into ranking boosts
- Migration 016_add_recent_search_history.sql added
- Focused unit and integration coverage for persistence, clear-all behavior, and interaction-driven ranking

Session: b06ad641-c9b7-4ab7-bfef-034c158d2688

## 2026-08-02 — Issue #212 A6 DB-backed hybrid search (Neo)

Implemented issue #212 ([App A6] DB-backed hybrid search) on branch `squad/212-db-hybrid-search` (branched from `feat/application-truth`). PR #230 (base `feat/application-truth`): https://github.com/matthewcorven/streaming-digest/pull/230. **Requesting independent adversarial review — no self-merge.**

**What shipped:**
- Replaced fixture-corpus `SearchUiService` DI default with real DB-backed hybrid search. Fixture corpus retained only for unit tests + recall harness.
- **Text leg:** generated `fts_body tsvector` column on `search_documents` (title_effective + body_effective) + GIN index; queried via `websearch_to_tsquery` + `ts_rank_cd`. (Migration 019.) This directly applies the 11.3b latency lesson — materialized tsvector, not per-query construction.
- **Vector leg:** pgvector cosine distance (`<=>`) between stored query embedding (`search_query_embeddings`) and `embeddings.embedding`, conditional on a stored query embedding row.
- **Aggregation:** one cluster per video via the existing pure `HybridRankingService` (max + top-3 avg + coverage, note boost, interaction boost, relative similarity normalization).
- **Empty corpus → waiting state** (PRD §2.10): readiness probe counts search_documents with succeeded embeddings; when 0, return empty results + "No searchable corpus yet. Run ingestion to populate search." — **never fabricated**.
- **Related items:** `IVideoClusterEmbeddingStore.GetRelatedVideosAsync` → `SearchRelatedItemResponse` with `RelativeSimilarityPercent` (progressive enhancement, try/catch — never fails search).
- **Interim model-readiness degrade:** embedding call in `PostgresRecentSearchStore.StoreSearchAsync` wrapped in try/catch; on failure, store recent search without embedding and proceed text-only. Awaiting `IModelReadinessGuard` (model plan WS-7).
- New Application seam `ISearchCorpusSearcher` + Infrastructure `PostgresSearchCorpusSearcher` (CTE-based hybrid query, scores clamped to [0,1], filters applied in outer query after CTEs select from search_documents).
- DI: `ISearchCorpusSearcher` singleton; `SearchUiService` DB-backed constructor; `IVideoClusterEmbeddingStore` bumped scoped→singleton (SearchUiService is singleton; NpgsqlDataSource is thread-safe).
- `IRecentSearchStore.GetQueryEmbeddingAsync(recentSearchId)` + `StoredQueryEmbedding` record added so the service can fetch the stored query embedding row for the vector leg.

**Deferred (with rationale):**
- **HNSW/IVFFlat vector indexes NOT added.** The `embeddings.embedding` column is dimensionless `vector` (multi-model by design — different providers have different dimensions). Both HNSW and IVFFlat require `vector(N)`. Migration 019's original HNSW indexes failed with `PostgresException 22023: column does not have dimensions`. Fix: dropped the HNSW indexes from the migration; vector search uses exact nearest-neighbour scan (sequential `<=>`). Correctness is unaffected — only large-scale latency. This refines ADR-0016's scope: HNSW-as-MVP applies once the embedding column is specialised per model dimension (model plan). Tracked there, not here.
- E2E slice: A6 requires unit + integration only per plan §10.3.

**Test gate (plan §10.3):** Unit 406 passed · Integration 72 passed, 1 skipped (pre-existing network skip) · Build 0 errors / 0 warnings.

**Npgsql+pgvector quirk reused from 11.3a/11.3b:** test connections that write `Pgvector.Vector` parameters need `NpgsqlDataSourceBuilder.UseVector()` (or `NpgsqlConnection.GlobalTypeMapper.UseVector()`) — bare `NpgsqlConnection` throws `Writing values of 'Pgvector.Vector' is not supported` on parameter binding. Applied in `DbHybridSearchIntegrationTests`.

**Test-suite hygiene:** `AuthFlowIntegrationTests.Search_endpoint_returns_one_cluster_per_video_and_uses_the_effective_title` previously asserted the fixture corpus result via the search endpoint. With the DI default now DB-backed, the endpoint correctly returns empty on an empty DB. That suite verifies auth/CSRF/endpoint wiring (not DB search correctness — `DbHybridSearchIntegrationTests` owns that), so its factory now overrides `SearchUiService` back to the fixture constructor via `RemoveAll<SearchUiService>() + AddSingleton(new SearchUiService(SearchUiCorpusCatalog.CreateDefaultFixtureCorpus()))`, matching the recall-harness pattern.

**Review artifacts emitted:** (1) decision record `Neo-a6-db-backed-hybrid-search-implemented-for-212-pr-.md` via `squad_decide`; (2) this history entry. Requesting independent adversarial review from Morpheus — **do not self-merge**.
