CREATE TABLE IF NOT EXISTS public.video_transcripts (
    id uuid PRIMARY KEY,
    video_id uuid NOT NULL REFERENCES public.videos (id) ON DELETE CASCADE,
    source_type text NOT NULL,
    language_code text NULL,
    is_auto_generated boolean NULL,
    is_active boolean NOT NULL DEFAULT true,
    model_name text NULL,
    engine_name text NULL,
    full_text_original text NOT NULL,
    full_text_override text NULL,
    confidence numeric NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.transcript_cues (
    id uuid PRIMARY KEY,
    transcript_id uuid NOT NULL REFERENCES public.video_transcripts (id) ON DELETE CASCADE,
    sequence integer NOT NULL,
    start_seconds numeric NOT NULL,
    end_seconds numeric NULL,
    text_original text NOT NULL,
    text_override text NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_video_transcripts_video_id
    ON public.video_transcripts (video_id);

CREATE UNIQUE INDEX IF NOT EXISTS idx_video_transcripts_video_id_active
    ON public.video_transcripts (video_id)
    WHERE is_active = true;

CREATE UNIQUE INDEX IF NOT EXISTS idx_transcript_cues_transcript_id_sequence
    ON public.transcript_cues (transcript_id, sequence);
