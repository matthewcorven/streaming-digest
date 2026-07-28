DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_trgm') THEN
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'unaccent') THEN
        CREATE EXTENSION IF NOT EXISTS unaccent;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
        EXECUTE $exec1$
            ALTER TABLE public.videos ADD COLUMN IF NOT EXISTS embedding_vector vector(384);

            DROP MATERIALIZED VIEW IF EXISTS public.video_search_documents;
            CREATE MATERIALIZED VIEW public.video_search_documents AS
            SELECT
                s.id,
                s.title_original,
                s.description_original,
                to_tsvector('english', coalesce(s.title_original, '') || ' ' || coalesce(s.description_original, '')) AS search_vector,
                coalesce(s.title_original, '') || ' ' || coalesce(s.description_original, '') AS search_text,
                s.embedding_vector
            FROM public.videos AS s;

            CREATE UNIQUE INDEX IF NOT EXISTS idx_video_search_documents_id ON public.video_search_documents (id);
            CREATE INDEX IF NOT EXISTS idx_video_search_documents_tsv ON public.video_search_documents USING gin (search_vector);
            CREATE INDEX IF NOT EXISTS idx_video_search_documents_search_text_trgm
                ON public.video_search_documents
                USING gin (search_text gin_trgm_ops);

            CREATE INDEX IF NOT EXISTS idx_video_search_documents_embedding_vector
                ON public.video_search_documents
                USING hnsw (embedding_vector vector_cosine_ops);

            CREATE OR REPLACE FUNCTION public.search_videos(query_text text, query_vector vector(384) DEFAULT NULL::vector(384), limit_count integer DEFAULT 10)
            RETURNS TABLE (
                video_id uuid,
                title text,
                description text,
                text_rank real,
                vector_similarity double precision
            )
            LANGUAGE sql
            AS $func1$
                WITH query_terms AS (
                    SELECT coalesce(query_text, '') AS normalized_query_text
                ),
                scored AS (
                    SELECT
                       s.id AS video_id,
                       s.title_original AS title,
                       s.description_original AS description,
                       s.search_vector,
                       q.normalized_query_text,
                       GREATEST(
                           similarity(coalesce(s.title_original, ''), q.normalized_query_text)::real,
                           similarity(coalesce(s.description_original, ''), q.normalized_query_text)::real,
                           similarity(s.search_text, q.normalized_query_text)::real
                       ) AS trigram_similarity,
                       CASE
                           WHEN query_vector IS NULL OR s.embedding_vector IS NULL THEN 0.0::double precision
                           ELSE greatest(0.0::double precision, 1.0::double precision - (s.embedding_vector <=> query_vector))
                       END AS vector_similarity
                    FROM public.video_search_documents AS s
                    CROSS JOIN query_terms AS q
                    WHERE q.normalized_query_text = ''
                       OR s.search_vector @@ websearch_to_tsquery('english', q.normalized_query_text)
                       OR GREATEST(
                           similarity(coalesce(s.title_original, ''), q.normalized_query_text)::real,
                           similarity(coalesce(s.description_original, ''), q.normalized_query_text)::real,
                           similarity(s.search_text, q.normalized_query_text)::real
                       ) >= 0.15
                ),
                ranked AS (
                    SELECT
                       video_id,
                       title,
                       description,
                       CASE
                           WHEN normalized_query_text = '' THEN 0.0::real
                           WHEN search_vector @@ websearch_to_tsquery('english', normalized_query_text) THEN
                               GREATEST(
                                   ts_rank_cd(search_vector, websearch_to_tsquery('english', normalized_query_text)),
                                   trigram_similarity
                               )
                           ELSE trigram_similarity
                       END AS text_rank,
                       vector_similarity
                    FROM scored
                )
                SELECT
                    video_id,
                    title,
                    description,
                    text_rank,
                    vector_similarity
                FROM ranked
                ORDER BY
                    CASE
                       WHEN query_vector IS NULL THEN text_rank::double precision
                       ELSE greatest(text_rank::double precision, vector_similarity)
                    END DESC,
                    vector_similarity DESC,
                    title ASC
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
                coalesce(v.title_original, '') || ' ' || coalesce(v.description_original, '') AS search_text,
                NULL::text AS embedding_vector
            FROM public.videos AS v;

            CREATE UNIQUE INDEX IF NOT EXISTS idx_video_search_documents_id ON public.video_search_documents (id);
            CREATE INDEX IF NOT EXISTS idx_video_search_documents_tsv ON public.video_search_documents USING gin (search_vector);
            CREATE INDEX IF NOT EXISTS idx_video_search_documents_search_text_trgm
                ON public.video_search_documents
                USING gin (search_text gin_trgm_ops);

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
                WITH query_terms AS (
                    SELECT coalesce(query_text, '') AS normalized_query_text
                ),
                scored AS (
                    SELECT
                        s.id AS video_id,
                        s.title_original AS title,
                        s.description_original AS description,
                        s.search_vector,
                        q.normalized_query_text,
                        GREATEST(
                            similarity(coalesce(s.title_original, ''), q.normalized_query_text)::real,
                            similarity(coalesce(s.description_original, ''), q.normalized_query_text)::real,
                            similarity(s.search_text, q.normalized_query_text)::real
                        ) AS trigram_similarity
                    FROM public.video_search_documents AS s
                    CROSS JOIN query_terms AS q
                    WHERE q.normalized_query_text = ''
                       OR s.search_vector @@ websearch_to_tsquery('english', q.normalized_query_text)
                       OR GREATEST(
                            similarity(coalesce(s.title_original, ''), q.normalized_query_text)::real,
                            similarity(coalesce(s.description_original, ''), q.normalized_query_text)::real,
                            similarity(s.search_text, q.normalized_query_text)::real
                        ) >= 0.15
                ),
                ranked AS (
                    SELECT
                        video_id,
                        title,
                        description,
                        CASE
                            WHEN normalized_query_text = '' THEN 0.0::real
                            WHEN search_vector @@ websearch_to_tsquery('english', normalized_query_text) THEN
                                GREATEST(
                                    ts_rank_cd(search_vector, websearch_to_tsquery('english', normalized_query_text)),
                                    trigram_similarity
                                )
                            ELSE trigram_similarity
                        END AS text_rank
                    FROM scored
                )
                SELECT
                    video_id,
                    title,
                    description,
                    text_rank,
                    0.0::double precision AS vector_similarity
                FROM ranked
                ORDER BY text_rank DESC
                LIMIT greatest(coalesce(limit_count, 10), 1);
            $func2$;
        $exec2$;
    END IF;
END $$;
