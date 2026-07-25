# MVP vector index is HNSW, not IVFFlat

DATA_MODEL §3.22 specified the `embeddings` table with the index choice left open: "HNSW or IVFFlat vector index depending pgvector version and dataset size." The Task 11.3a prototype (evidence in `docs/verification/11.3a-vector-knowledge-base.md` + `.json`, spike `spikes/StreamingDigest.VectorPrototype/`) measured both indexes on the real Aspire-managed stack — PostgreSQL 17.10 + pgvector 0.8.5 — at the MVP corpus scale (500 videos / 11,958 vectors / dim=384 / seed=42):

| Index | Build | Avg query | Recall@10 | Size |
|-------|------:|----------:|----------:|-----:|
| HNSW (pgvector defaults) | 979 ms | 0.522 ms | 0.99 | 23.9 MB |
| IVFFlat (lists=100, probes=10) | 422 ms | 0.509 ms | 0.98 | 19.6 MB |
| Exact seq-scan (no index) | — | 3.22 ms | 1.00 | — |

We decided: **the `embeddings` vector index is HNSW**, built with pgvector defaults (no `lists`/`probes` tuning), in the migration that creates the table.

The choice turns on operability, not on the measured quality delta — which is negligible. IVFFlat's build-time and size advantages are real (~2.3× faster build, ~1.22× smaller), but IVFFlat requires the table to be pre-populated before index creation and requires choosing and re-tuning `lists`/`probes` as the corpus grows. pgvector 0.8.5 builds HNSW incrementally — the index is correct from the first inserted row with no empty-index window and no tuning parameters — which is the right default for a self-hosted, single-user product whose operator never thinks about index maintenance. Query latency is statistically indistinguishable (0.522 ms vs 0.509 ms), and both are 6× faster than the exact seq-scan at this scale.

This closes the open choice in DATA_MODEL §3.22 rather than overturning an existing decision. It is recorded as an ADR — over the 11.3a prototyper's initial "no ADR, refinement only" call — because the index type is baked into the migration that creates `embeddings` (reversing it at production scale means an index rebuild under write load), because §3.22's open text gives Task 11.3's implementer no recorded basis for the pick, and because a verification report is evidence, not a decision record: the implementer reads DATA_MODEL and the ADR index, not a prototype's findings section.

## Considered options

- **IVFFlat (lists=100, probes=10)** — rejected for MVP: requires pre-population before build (empty-index window or deferred creation), and `lists`/`probes` must be chosen and re-tuned as the corpus grows. Its advantages (422 ms build, 19.6 MB) are immaterial at MVP scale. It remains the documented fallback if a future measurement reverses the trade-off.
- **Exact KNN (no index)** — rejected: 3.22 ms/query at MVP scale with linear growth; fine for correctness checks, not the production path.
- **Defer the choice to Task 11.3/11.4** — rejected: §3.22 already deferred it once; the measurement now exists, and leaving it open again just moves an answered question onto the implementer.

## Consequences

- DATA_MODEL §3.22 names HNSW (with this ADR as the reference) and §3.25 `search_query_embeddings` follows the same default; the Task 11.4 migration creates the index with pgvector defaults — no `WITH (lists=...)` clause, no `probes` setting anywhere.
- **Scope limit (honest boundary):** the recall figures above are internal-consistency checks from a synthetic embedder with tight, well-separated topic clusters — they are not a semantic-quality claim, and they flatter both indexes equally. The HNSW choice rests on build/query mechanics and operability, which the synthetic setup does measure faithfully.
- **Still owed:** re-measurement at ~2,000 videos / ~48k vectors is owned by Task 12.8 (#32), alongside the search latency targets. If HNSW build time, size, or recall degrades past the IVFFlat trade-off there, this ADR is revisited with IVFFlat as the named fallback — that is a scale decision, not a reopening of the MVP default.
- The pgvector 0.8.5 incremental-build behavior this decision relies on is version-specific; a pgvector major upgrade re-verifies it (alongside the ADR-0008/0011 embedding-transition machinery).
- This ADR closes the "HNSW vs IVFFlat" open implementation decision from Task 11.3a (#18).
