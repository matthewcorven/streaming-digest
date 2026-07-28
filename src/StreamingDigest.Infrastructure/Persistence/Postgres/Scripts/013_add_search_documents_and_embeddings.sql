CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS public.search_documents (
    id uuid PRIMARY KEY,
    document_type text NOT NULL,
    source_entity_type text NOT NULL,
    source_entity_id uuid NOT NULL,
    source_field_name text NULL,
    chunk_index integer NULL,
    chunk_start_offset integer NULL,
    chunk_end_offset integer NULL,
    token_count integer NULL,
    search_weight numeric NULL,
    embedding_required boolean NOT NULL DEFAULT true,
    parent_video_id uuid NULL REFERENCES public.videos(id) ON DELETE CASCADE,
    parent_segment_id uuid NULL REFERENCES public.segments(id) ON DELETE CASCADE,
    title_effective text NULL,
    body_effective text NULL,
    metadata_json jsonb NULL,
    fts_weight_config jsonb NULL,
    content_hash text NOT NULL,
    is_stale boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_search_documents_identity UNIQUE NULLS NOT DISTINCT (
        source_entity_type,
        source_entity_id,
        source_field_name,
        chunk_index,
        content_hash
    )
);

CREATE INDEX IF NOT EXISTS idx_search_documents_source_entity
    ON public.search_documents (source_entity_type, source_entity_id);

CREATE INDEX IF NOT EXISTS idx_search_documents_parent_video_id
    ON public.search_documents (parent_video_id);

CREATE INDEX IF NOT EXISTS idx_search_documents_document_type
    ON public.search_documents (document_type);

CREATE TABLE IF NOT EXISTS public.embeddings (
    id uuid PRIMARY KEY,
    search_document_id uuid NOT NULL REFERENCES public.search_documents(id) ON DELETE CASCADE,
    provider text NOT NULL,
    model text NOT NULL,
    dimensions integer NOT NULL,
    content_hash text NOT NULL,
    source_text_hash text NULL,
    embedding vector NOT NULL,
    embedding_status text NOT NULL DEFAULT 'succeeded',
    error_summary text NULL,
    generated_by_operation_id uuid NULL REFERENCES public.operations(id),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_embeddings_status CHECK (embedding_status IN ('pending', 'succeeded', 'failed')),
    CONSTRAINT uq_embeddings_identity UNIQUE (search_document_id, provider, model, dimensions, content_hash)
);

CREATE INDEX IF NOT EXISTS idx_embeddings_provider_model_dimensions
    ON public.embeddings (provider, model, dimensions);

CREATE INDEX IF NOT EXISTS idx_embeddings_content_hash
    ON public.embeddings (content_hash);
