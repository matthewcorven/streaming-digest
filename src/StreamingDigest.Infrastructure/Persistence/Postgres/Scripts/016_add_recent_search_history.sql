CREATE TABLE IF NOT EXISTS public.recent_searches (
    id uuid PRIMARY KEY,
    query_text text NOT NULL,
    searched_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    text_weight numeric NOT NULL,
    vector_weight numeric NOT NULL,
    filters_json jsonb NULL
);

CREATE INDEX IF NOT EXISTS idx_recent_searches_searched_at
    ON public.recent_searches (searched_at DESC);

CREATE TABLE IF NOT EXISTS public.search_query_embeddings (
    id uuid PRIMARY KEY,
    recent_search_id uuid NOT NULL REFERENCES public.recent_searches(id) ON DELETE CASCADE,
    provider text NOT NULL,
    model text NOT NULL,
    dimensions integer NOT NULL,
    content_hash text NOT NULL,
    embedding vector NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_search_query_embeddings_identity UNIQUE (
        recent_search_id,
        provider,
        model,
        dimensions,
        content_hash
    )
);

CREATE INDEX IF NOT EXISTS idx_search_query_embeddings_provider_model_dimensions
    ON public.search_query_embeddings (provider, model, dimensions);

CREATE INDEX IF NOT EXISTS idx_search_query_embeddings_recent_search_id
    ON public.search_query_embeddings (recent_search_id);

CREATE TABLE IF NOT EXISTS public.user_interaction_events (
    id uuid PRIMARY KEY,
    recent_search_id uuid NULL REFERENCES public.recent_searches(id) ON DELETE SET NULL,
    video_id uuid NULL REFERENCES public.videos(id) ON DELETE CASCADE,
    search_document_id uuid NULL REFERENCES public.search_documents(id) ON DELETE CASCADE,
    result_type text NOT NULL,
    event_type text NOT NULL,
    activated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    metadata_json jsonb NULL
);

CREATE INDEX IF NOT EXISTS idx_user_interaction_events_video_id_activated_at
    ON public.user_interaction_events (video_id, activated_at DESC);

CREATE INDEX IF NOT EXISTS idx_user_interaction_events_recent_search_id
    ON public.user_interaction_events (recent_search_id);
