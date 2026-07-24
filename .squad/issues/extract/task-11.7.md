### Task 11.7: Implement video-cluster aggregate embeddings

Requirements:

- Build `video_cluster_embeddings` from normalized child document embeddings that share provider, model, and dimensions.
- Store content hash, provider/model/dimensions, component weights, stale state, and operation provenance.
- Use aggregate cluster vectors for high-signal digest matching and coarse related-item discovery.
- Do not use aggregate cluster vectors as the only search index; fine-grained `search_documents` remain the primary search units.
- Mark cluster embeddings stale when child search documents, notes, overrides, or active embedding model changes require invalidation.

Verification:

- Integration test creates a cluster embedding after document embeddings exist.
- Editing a note/title/transcript marks only the affected document(s) and parent cluster aggregate stale.
- High-signal digest matching ignores mismatched provider/model/dimension vectors.

## Search performance targets

MVP corpus assumption is fewer than 500 videos in PostgreSQL, while design should remain reasonable up to about 2,000 videos. Show a spinner or progress state after 1 second.

Latency targets:

- Fewer than 500 videos in DB: P50 <= 2 seconds, P95 <= 5 seconds.
- Up to 2,000 videos in DB: P50 <= 3 seconds, P95 <= 10 seconds.

## Phase 12: Hybrid search

