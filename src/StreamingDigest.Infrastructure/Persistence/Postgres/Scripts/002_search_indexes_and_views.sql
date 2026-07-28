DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_trgm') THEN
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.search_documents (
    id uuid PRIMARY KEY,
    document_type text NOT NULL,
    source_entity_type text NOT NULL,
    source_entity_id uuid NOT NULL,
    parent_video_id uuid NULL,
    title_effective text NOT NULL DEFAULT '',
    body_effective text NOT NULL DEFAULT '',
    content_hash text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_search_documents_parent_video_id
    ON public.search_documents (parent_video_id);

CREATE INDEX IF NOT EXISTS idx_search_documents_document_type
    ON public.search_documents (document_type);

CREATE INDEX IF NOT EXISTS idx_search_documents_tsv
    ON public.search_documents
    USING gin (to_tsvector('english', coalesce(title_effective, '') || ' ' || coalesce(body_effective, '')));

CREATE INDEX IF NOT EXISTS idx_search_documents_title_trgm
    ON public.search_documents
    USING gin (coalesce(title_effective, '') gin_trgm_ops);

CREATE INDEX IF NOT EXISTS idx_search_documents_body_trgm
    ON public.search_documents
    USING gin (coalesce(body_effective, '') gin_trgm_ops);

CREATE OR REPLACE FUNCTION public.search_videos(query_text text, limit_count integer DEFAULT 10)
RETURNS TABLE (
    video_id uuid,
    title text,
    description text,
    text_rank real,
    trigram_similarity double precision
)
LANGUAGE sql
AS $$
    WITH ranked_documents AS (
        SELECT
            s.parent_video_id AS video_id,
            s.title_effective AS title,
            s.body_effective AS description,
            ts_rank_cd(
                setweight(to_tsvector('english', coalesce(s.title_effective, '')), 'A') ||
                setweight(to_tsvector('english', coalesce(s.body_effective, '')), 'B'),
                websearch_to_tsquery('english', coalesce(query_text, ''))
            ) AS text_rank,
            GREATEST(
                similarity(s.title_effective, coalesce(query_text, '')),
                similarity(s.body_effective, coalesce(query_text, ''))
            ) AS trigram_similarity
        FROM public.search_documents AS s
        WHERE coalesce(query_text, '') = ''
           OR (
                setweight(to_tsvector('english', coalesce(s.title_effective, '')), 'A') ||
                setweight(to_tsvector('english', coalesce(s.body_effective, '')), 'B')
            ) @@ websearch_to_tsquery('english', query_text)
           OR similarity(s.title_effective, coalesce(query_text, '')) > 0.1
           OR similarity(s.body_effective, coalesce(query_text, '')) > 0.1
    )
    SELECT
        video_id,
        title,
        description,
        text_rank,
        trigram_similarity
    FROM ranked_documents
    ORDER BY text_rank DESC, trigram_similarity DESC, video_id
    LIMIT greatest(coalesce(limit_count, 10), 1);
$$;
