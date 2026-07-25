CREATE TABLE IF NOT EXISTS public.digests (
    id uuid PRIMARY KEY,
    ingestion_run_id uuid NOT NULL UNIQUE REFERENCES public.ingestion_runs(id),
    run_type text NOT NULL,
    payload_json jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_digests_ingestion_run_id ON public.digests (ingestion_run_id);
