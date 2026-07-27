CREATE TABLE IF NOT EXISTS public.segment_generations (
    id uuid PRIMARY KEY,
    video_id uuid NOT NULL REFERENCES public.videos(id),
    source_type text NOT NULL,
    generation_version integer NOT NULL,
    is_active boolean NOT NULL DEFAULT false,
    requires_user_approval boolean NOT NULL DEFAULT false,
    status text NOT NULL,
    llm_model text NULL,
    llm_prompt_version text NULL,
    created_by_operation_id uuid NULL REFERENCES public.operations(id),
    activated_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_segment_generations_video_version
    ON public.segment_generations (video_id, generation_version);

CREATE INDEX IF NOT EXISTS idx_segment_generations_video_id
    ON public.segment_generations (video_id);

CREATE TABLE IF NOT EXISTS public.segments (
    id uuid PRIMARY KEY,
    video_id uuid NOT NULL REFERENCES public.videos(id),
    segment_generation_id uuid NOT NULL REFERENCES public.segment_generations(id),
    source_type text NOT NULL,
    sequence integer NOT NULL,
    start_seconds numeric NOT NULL,
    end_seconds numeric NULL,
    title_original text NOT NULL,
    title_override text NULL,
    summary_original text NULL,
    summary_override text NULL,
    llm_model text NULL,
    llm_prompt_version text NULL,
    is_active boolean NOT NULL DEFAULT true,
    requires_embedding_approval boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_segments_generation_sequence
    ON public.segments (segment_generation_id, sequence);

CREATE INDEX IF NOT EXISTS idx_segments_video_start_seconds
    ON public.segments (video_id, start_seconds);

CREATE INDEX IF NOT EXISTS idx_segments_video_is_active
    ON public.segments (video_id, is_active);
