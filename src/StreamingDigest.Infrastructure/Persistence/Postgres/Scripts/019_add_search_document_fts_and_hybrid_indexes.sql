-- Issue #212: DB-backed hybrid search support.
-- Adds a generated tsvector column to search_documents so the corpus can be queried with
-- websearch_to_tsquery / ts_rank_cd without a separate materialized view, plus a GIN index
-- for the text leg. Vector similarity uses pgvector's cosine distance (<=>) operator; the
-- embeddings.embedding column is dimensionless (it stores vectors from multiple model
-- dimensions), so an HNSW/IVFFlat index cannot be created over it directly. Vector search
-- therefore uses an exact nearest-neighbour scan for now. Once the embedding column is
-- specialised per model dimension (model plan), an HNSW index scoped to that dimension can
-- be added here.

-- Generated tsvector over the effective title+body of every search document. STORED so it
-- is updated automatically whenever title_effective / body_effective change. A simple
-- english configuration keeps the search UX predictable; document-specific weighting can be
-- added later via fts_weight_config.
ALTER TABLE public.search_documents
    ADD COLUMN IF NOT EXISTS fts_body tsvector
    GENERATED ALWAYS AS (
        to_tsvector('english', coalesce(title_effective, '') || ' ' || coalesce(body_effective, ''))
    ) STORED;

CREATE INDEX IF NOT EXISTS idx_search_documents_fts_body
    ON public.search_documents USING GIN (fts_body);

-- Handy btree support for the "empty corpus" readiness probe (count of searchable docs)
-- and the per-video grouping join used by the hybrid query.
CREATE INDEX IF NOT EXISTS idx_search_documents_parent_video_active
    ON public.search_documents (parent_video_id)
    WHERE is_stale = FALSE;
