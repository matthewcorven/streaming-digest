# Shared-resource search documents are duplicated per referencing video

A canonical external resource (e.g. a GitHub README) can be linked from many videos, but `search_documents.parent_video_id` points at exactly one video. We considered a single document with multi-parent attribution versus one document per referencing video.

We decided: Video Cluster is a search-time aggregation over documents sharing a `parent_video_id`, and a shared resource's documents are stored once per referencing video (identical content and content hash, distinct `parent_video_id`).

## Consequences

- Cluster membership and scoring stay uniform: every search document has exactly one parent video, so the cluster formula never special-cases shared resources.
- The same README match legitimately boosts every video cluster that links it — matching the PRD expectation that shared repos appear in multiple clusters.
- Storage cost is trivial at MVP scale (< 2,000 videos). Content-hash deduplication keeps the *embedding* from being computed twice for identical text; only the document rows duplicate.
- Deletion of one channel removes only that channel's document copies; the canonical resource and other videos' copies survive (consistent with DATA_MODEL §9).
- The stored `video_cluster_embeddings` row remains the persistent per-video fingerprint for digest matching and related-item discovery — distinct from the search-time cluster construct.
- `DATA_MODEL.md` §3.21 and §5 should note the duplication convention explicitly.
