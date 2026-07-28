CREATE TABLE IF NOT EXISTS public.external_resources (
    id uuid PRIMARY KEY,
    canonical_url text NOT NULL,
    final_url text NULL,
    domain text NULL,
    resource_type text NOT NULL DEFAULT 'unknown',
    title_original text NULL,
    title_override text NULL,
    description_original text NULL,
    description_override text NULL,
    classification_original text NOT NULL DEFAULT 'unknown',
    classification_override text NULL,
    classification_confidence numeric NULL,
    classification_method text NULL,
    is_ad_or_sponsor boolean NOT NULL DEFAULT false,
    raw_metadata_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_external_resources_canonical_url
    ON public.external_resources (canonical_url);

CREATE TABLE IF NOT EXISTS public.repositories (
    id uuid PRIMARY KEY,
    host text NOT NULL,
    canonical_url text NOT NULL,
    owner text NULL,
    name text NULL,
    normalized_owner text NULL,
    normalized_name text NULL,
    default_branch text NULL,
    description_original text NULL,
    description_override text NULL,
    stars integer NULL,
    forks integer NULL,
    primary_language text NULL,
    topics text[] NULL,
    license_spdx_id text NULL,
    deepwiki_url text NULL,
    deepwiki_checked_at timestamptz NULL,
    raw_metadata_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_repositories_canonical_url
    ON public.repositories (canonical_url);

CREATE TABLE IF NOT EXISTS public.field_override_history (
    id uuid PRIMARY KEY,
    entity_type text NOT NULL,
    entity_id uuid NOT NULL,
    field_name text NOT NULL,
    previous_value text NULL,
    new_value text NULL,
    changed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_field_override_history_entity
    ON public.field_override_history (entity_type, entity_id);

CREATE INDEX IF NOT EXISTS idx_field_override_history_changed_at
    ON public.field_override_history (changed_at);

-- Ensure videos.title_override column exists (was in the data model from day one but
-- not explicitly added in a previous migration if it was omitted from the baseline)
ALTER TABLE public.videos ADD COLUMN IF NOT EXISTS title_override text NULL;
