CREATE TABLE IF NOT EXISTS public.media_artifacts (
    id uuid PRIMARY KEY,
    owner_type text NOT NULL,
    owner_id uuid NOT NULL,
    artifact_kind text NOT NULL,
    file_path text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_media_artifacts_owner_type_owner_id
    ON public.media_artifacts (owner_type, owner_id);

CREATE INDEX IF NOT EXISTS idx_media_artifacts_artifact_kind
    ON public.media_artifacts (artifact_kind);

CREATE UNIQUE INDEX IF NOT EXISTS idx_media_artifacts_file_path
    ON public.media_artifacts (file_path);
