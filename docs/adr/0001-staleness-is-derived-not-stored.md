# Staleness is derived, not stored

Search documents, embeddings, and video-cluster embeddings can all become "stale" (out of date with their source content or the active embedding model). We considered storing staleness as explicit flags on each row (`search_documents.is_stale`, `embeddings.embedding_status = 'stale'`, `video_cluster_embeddings.is_stale`).

We decided staleness is a derived condition with a single source of truth instead: a search document is stale when its stored content hash no longer matches the Effective Value of its source entity; an embedding is stale when its parent search document is stale or when the active provider/model/dimensions differ from those it was generated with. Embedding status columns record only job outcome (`succeeded`/`failed`/`pending`).

## Consequences

- Invalidation rules (DATA_MODEL §7) become mechanically checkable: staleness = content-hash mismatch or model mismatch, nothing else.
- A whole class of "flags out of sync" bugs (document stale but embedding not, and vice versa) is eliminated.
- Queries that need "all stale items" compute staleness via hash/model comparison rather than reading a flag; a computed view or function can keep this ergonomic.
- `DATA_MODEL.md` and `API_SPEC.md` should be updated to match: `embeddings.embedding_status` drops `'stale'`; `is_stale` flags become computed/read-model projections rather than written state.
