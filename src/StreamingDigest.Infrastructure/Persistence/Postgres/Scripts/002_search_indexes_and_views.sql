DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
        EXECUTE $exec1$
            CREATE MATERIALIZED VIEW IF NOT EXISTS public.video_search_documents AS
            SELECT
                v.id,
                v.title_original,
                v.description_original,
                to_tsvector('english', coalesce(v.title_original, '') || ' ' || coalesce(v.description_original, '')) AS search_vector,
                NULL::vector AS embedding_vector
            FROM public.videos AS v;

            CREATE UNIQUE INDEX IF NOT EXISTS idx_video_search_documents_id ON public.video_search_documents (id);
            CREATE INDEX IF NOT EXISTS idx_video_search_documents_tsv ON public.video_search_documents USING gin (search_vector);

            CREATE INDEX IF NOT EXISTS idx_video_search_documents_embedding_vector
                ON public.video_search_documents
                USING hnsw (embedding_vector vector_cosine_ops);

            CREATE OR REPLACE FUNCTION public.search_videos(query_text text, query_vector vector DEFAULT NULL::vector, limit_count integer DEFAULT 10)
            RETURNS TABLE (
                video_id uuid,
                title text,
                description text,
                text_rank real,
                vector_similarity double precision
            )
            LANGUAGE sql
            AS $func1$
                SELECT
                    s.id AS video_id,
                    s.title_original AS title,
                    s.description_original AS description,
                    ts_rank_cd(s.search_vector, websearch_to_tsquery('english', coalesce(query_text, ''))) AS text_rank,
                    0.0::double precision AS vector_similarity
                FROM public.video_search_documents AS s
                WHERE coalesce(query_text, '') = '' OR s.search_vector @@ websearch_to_tsquery('english', query_text)
                ORDER BY text_rank DESC
                LIMIT greatest(coalesce(limit_count, 10), 1);
            $func1$;
        $exec1$;
    ELSE
        EXECUTE $exec2$
            CREATE MATERIALIZED VIEW IF NOT EXISTS public.video_search_documents AS
            SELECT
                v.id,
                v.title_original,
                v.description_original,
                to_tsvector('english', coalesce(v.title_original, '') || ' ' || coalesce(v.description_original, '')) AS search_vector,
                NULL::text AS embedding_vector
            FROM public.videos AS v;

            CREATE UNIQUE INDEX IF NOT EXISTS idx_video_search_documents_id ON public.video_search_documents (id);
            CREATE INDEX IF NOT EXISTS idx_video_search_documents_tsv ON public.video_search_documents USING gin (search_vector);

            CREATE OR REPLACE FUNCTION public.search_videos(query_text text, query_vector text DEFAULT NULL::text, limit_count integer DEFAULT 10)
            RETURNS TABLE (
                video_id uuid,
                title text,
                description text,
                text_rank real,
                vector_similarity double precision
            )
            LANGUAGE sql
            AS $func2$
                SELECT
                    s.id AS video_id,
                    s.title_original AS title,
                    s.description_original AS description,
                    ts_rank_cd(s.search_vector, websearch_to_tsquery('english', coalesce(query_text, ''))) AS text_rank,
                    0.0::double precision AS vector_similarity
                FROM public.video_search_documents AS s
                WHERE coalesce(query_text, '') = '' OR s.search_vector @@ websearch_to_tsquery('english', query_text)
                ORDER BY text_rank DESC
                LIMIT greatest(coalesce(limit_count, 10), 1);
            $func2$;
        $exec2$;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_trgm') THEN
        CREATE INDEX IF NOT EXISTS idx_video_search_documents_title_trgm
            ON public.video_search_documents
            USING gin (coalesce(title_original, '') gin_trgm_ops);
    END IF;
END $$;
