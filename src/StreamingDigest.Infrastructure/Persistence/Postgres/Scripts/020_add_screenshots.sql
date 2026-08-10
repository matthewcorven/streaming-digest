-- Issue #211: screenshots table per DATA_MODEL §3.12.
-- The A2 screenshots stage generates one WebP file per segment; this table records the
-- generated artifacts so they survive the pipeline (previously the rows were produced
-- against an EF-ignored nav with no backing table and silently vanished).

CREATE TABLE IF NOT EXISTS public.screenshots (
    id uuid PRIMARY KEY,
    video_id uuid NOT NULL REFERENCES public.videos(id),
    segment_id uuid NULL REFERENCES public.segments(id),
    timestamp_seconds numeric NOT NULL,
    file_path text NOT NULL,
    storage_key text NULL,
    public_url_path text NULL,
    mime_type text NOT NULL DEFAULT 'image/webp',
    width integer NULL,
    height integer NULL,
    file_size_bytes bigint NULL,
    content_hash text NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_screenshots_video_id
    ON public.screenshots (video_id);

CREATE INDEX IF NOT EXISTS idx_screenshots_segment_id
    ON public.screenshots (segment_id);
