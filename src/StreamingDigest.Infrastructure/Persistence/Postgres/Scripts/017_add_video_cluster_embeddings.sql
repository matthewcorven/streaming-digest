CREATE TABLE IF NOT EXISTS public.video_cluster_embeddings (
    id uuid PRIMARY KEY,
    video_id uuid NOT NULL REFERENCES public.videos(id) ON DELETE CASCADE,
    provider text NOT NULL,
    model text NOT NULL,
    dimensions integer NOT NULL,
    content_hash text NOT NULL,
    embedding vector NOT NULL,
    component_weights_json jsonb NOT NULL,
    is_stale boolean NOT NULL DEFAULT false,
    requires_user_approval boolean NOT NULL DEFAULT false,
    generated_by_operation_id uuid NULL REFERENCES public.operations(id),
    stale_marked_by_operation_id uuid NULL REFERENCES public.operations(id),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_video_cluster_embeddings_identity UNIQUE (video_id, provider, model, dimensions)
);

CREATE INDEX IF NOT EXISTS idx_video_cluster_embeddings_video_id
    ON public.video_cluster_embeddings (video_id);

CREATE INDEX IF NOT EXISTS idx_video_cluster_embeddings_provider_model_dimensions
    ON public.video_cluster_embeddings (provider, model, dimensions);

CREATE INDEX IF NOT EXISTS idx_video_cluster_embeddings_is_stale
    ON public.video_cluster_embeddings (is_stale);
