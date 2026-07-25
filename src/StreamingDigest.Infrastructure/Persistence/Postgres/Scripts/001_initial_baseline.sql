DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
        CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $$;

CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

CREATE TABLE IF NOT EXISTS public.app_users (
    id uuid PRIMARY KEY,
    username text NOT NULL UNIQUE,
    password_hash text NOT NULL,
    password_hash_algorithm text NOT NULL DEFAULT 'argon2id',
    must_change_password boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at timestamptz NULL
);

CREATE TABLE IF NOT EXISTS public.app_settings (
    key text PRIMARY KEY,
    value_json jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.operations (
    id uuid PRIMARY KEY,
    operation_type text NOT NULL,
    status text NOT NULL,
    risk_level text NULL,
    requested_by text NULL,
    related_entity_type text NULL,
    related_entity_id uuid NULL,
    hangfire_job_id text NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    summary_json jsonb NULL,
    error_summary text NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.channels (
    id uuid PRIMARY KEY,
    youtube_channel_id text NOT NULL UNIQUE,
    name_original text NOT NULL,
    name_override text NULL,
    profile_url text NOT NULL,
    source_url text NOT NULL,
    description_original text NULL,
    description_override text NULL,
    is_paused boolean NOT NULL DEFAULT false,
    default_max_age_days integer NULL,
    default_backfill_max_videos integer NULL,
    is_degraded boolean NOT NULL DEFAULT false,
    consecutive_failures integer NOT NULL DEFAULT 0,
    last_probe_at timestamptz NULL,
    degraded_at timestamptz NULL,
    last_ingested_at timestamptz NULL,
    last_ingestion_status text NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.ingestion_runs (
    id uuid PRIMARY KEY,
    operation_id uuid NULL REFERENCES public.operations(id),
    correlation_id text NULL,
    schedule_id text NULL,
    run_type text NOT NULL,
    triggered_by text NOT NULL,
    requested_by_user_id uuid NULL REFERENCES public.app_users(id),
    status text NOT NULL,
    started_at timestamptz NOT NULL,
    completed_at timestamptz NULL,
    channels_checked integer NOT NULL DEFAULT 0,
    new_videos_found integer NOT NULL DEFAULT 0,
    videos_ingested integer NOT NULL DEFAULT 0,
    videos_failed integer NOT NULL DEFAULT 0,
    videos_skipped integer NOT NULL DEFAULT 0,
    transcripts_found integer NOT NULL DEFAULT 0,
    transcripts_missing integer NOT NULL DEFAULT 0,
    repositories_found integer NOT NULL DEFAULT 0,
    config_snapshot_json jsonb NULL,
    summary_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.ingestion_items (
    id uuid PRIMARY KEY,
    ingestion_run_id uuid NOT NULL REFERENCES public.ingestion_runs(id),
    operation_id uuid NULL REFERENCES public.operations(id),
    item_type text NOT NULL,
    item_id uuid NULL,
    external_key text NULL,
    idempotency_key text NULL,
    depends_on_item_id uuid NULL REFERENCES public.ingestion_items(id),
    stage text NOT NULL,
    stage_version text NULL,
    job_payload_version text NULL,
    status text NOT NULL,
    attempt integer NOT NULL DEFAULT 0,
    retry_count integer NOT NULL DEFAULT 0,
    max_attempts integer NOT NULL DEFAULT 7,
    is_retryable boolean NOT NULL DEFAULT true,
    next_retry_at timestamptz NULL,
    deferred_until timestamptz NULL,
    deferment_reason text NULL,
    worker_id text NULL,
    started_by_job_id text NULL,
    completed_by_job_id text NULL,
    error_summary text NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.videos (
    id uuid PRIMARY KEY,
    platform text NOT NULL DEFAULT 'youtube',
    platform_video_url text NOT NULL,
    platform_video_id text NOT NULL,
    youtube_video_id text NOT NULL,
    channel_id uuid NOT NULL REFERENCES public.channels(id),
    author_original text NOT NULL,
    author_override text NULL,
    title_original text NOT NULL,
    title_override text NULL,
    description_original text NULL,
    description_override text NULL,
    video_url text NOT NULL,
    published_at timestamptz NULL,
    duration_seconds integer NULL,
    thumbnail_url text NULL,
    is_long_form boolean NOT NULL DEFAULT true,
    ingestion_status text NOT NULL DEFAULT 'pending',
    transcript_status text NOT NULL DEFAULT 'unknown',
    screenshot_status text NOT NULL DEFAULT 'unknown',
    processing_version text NULL,
    last_successful_ingestion_run_id uuid NULL REFERENCES public.ingestion_runs(id),
    last_failed_ingestion_run_id uuid NULL REFERENCES public.ingestion_runs(id),
    metadata_fetched_at timestamptz NULL,
    transcript_fetched_at timestamptz NULL,
    links_extracted_at timestamptz NULL,
    search_indexed_at timestamptz NULL,
    raw_metadata_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_videos_identity UNIQUE (platform, platform_video_url, platform_video_id)
);

CREATE TABLE IF NOT EXISTS public.domain_events (
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    severity text NOT NULL,
    entity_type text NULL,
    entity_id uuid NULL,
    ingestion_run_id uuid NULL REFERENCES public.ingestion_runs(id),
    operation_id uuid NULL REFERENCES public.operations(id),
    message text NOT NULL,
    details_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_channels_youtube_channel_id ON public.channels (youtube_channel_id);
CREATE INDEX IF NOT EXISTS idx_videos_channel_id ON public.videos (channel_id);
CREATE INDEX IF NOT EXISTS idx_videos_platform_video_id ON public.videos (platform, platform_video_id);
CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_status ON public.ingestion_items (ingestion_run_id, status);
CREATE INDEX IF NOT EXISTS idx_domain_events_ingestion_run_id ON public.domain_events (ingestion_run_id);
