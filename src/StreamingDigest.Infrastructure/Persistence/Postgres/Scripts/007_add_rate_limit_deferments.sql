CREATE TABLE IF NOT EXISTS public.rate_limit_deferments (
    id uuid PRIMARY KEY,
    scope_type character varying(64) NOT NULL,
    scope_key character varying(256) NOT NULL,
    reason character varying(512) NOT NULL,
    retry_after_at timestamptz NOT NULL,
    status character varying(32) NOT NULL,
    details_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    row_version bytea NULL
);

CREATE INDEX IF NOT EXISTS idx_rate_limit_deferments_scope_type_scope_key
    ON public.rate_limit_deferments (scope_type, scope_key);

CREATE INDEX IF NOT EXISTS idx_rate_limit_deferments_status
    ON public.rate_limit_deferments (status);

CREATE INDEX IF NOT EXISTS idx_rate_limit_deferments_retry_after_at
    ON public.rate_limit_deferments (retry_after_at);

CREATE INDEX IF NOT EXISTS idx_rate_limit_deferments_created_at
    ON public.rate_limit_deferments (created_at);
