ALTER TABLE public.videos
    ADD COLUMN IF NOT EXISTS chapters_json text NULL,
    ADD COLUMN IF NOT EXISTS captions_json text NULL;
