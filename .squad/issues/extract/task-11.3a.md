### Task 11.3a: Prototype vector knowledge-base approach

Source: `docs/architecture/DATA_MODEL.md` §5–7; ADR-0001, ADR-0004; `.agents/skills/prototype/SKILL.md` (logic branch)

Throwaway prototype validating the embedding-side knowledge-base approach before production implementation, using synthetic data per the prototype policy in the MVP scope conformance checklist.

Requirements:

- Programmatic synthetic corpus generator (template/topic/vocabulary driven, seedable, no AI) producing controlled-proportion documents: video metadata, segment titles/summaries, transcript chunks, external resource metadata, scraped page text, repository README chunks, and notes, with tunable topic distributions.
- Synthetic embedding strategy for bulk generation (e.g. deterministic hashing vectors or a tiny local bag-of-words model) so thousands of vectors are created locally with no provider calls; when the Task 11.3 provider exists, a small real-embedding validation subset calibrates the synthetic approach — the prototype runs standalone before that, per the early-prototype directive.
- Validates: document construction rules (DATA_MODEL §5), content-hash/staleness derivation (ADR-0001), ADR-0004 duplicate-per-parent-video cardinality with shared embeddings, video-cluster aggregate (centroid) construction from normalized child embeddings, and pgvector storage/index behavior at MVP-scale volume (HNSW vs IVFFlat trade-off evidence).
- Output: comparison findings and any model/construction corrections; if the outcome changes storage or index decisions, record an ADR (`docs/adr/`, next available number).

Verification:

- Prototype builds the synthetic corpus and embeddings with zero external AI calls.
- Comparison report committed per the Verification evidence convention; any resulting ADR recorded.

