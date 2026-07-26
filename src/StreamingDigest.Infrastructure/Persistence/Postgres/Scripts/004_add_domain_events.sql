CREATE TABLE IF NOT EXISTS public.domain_events (
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    severity text NOT NULL,
    entity_type text,
    entity_id uuid,
    ingestion_run_id uuid,
    operation_id uuid,
    message text NOT NULL,
    details_json jsonb,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_domain_events_ingestion_run_id ON public.domain_events (ingestion_run_id);
CREATE INDEX IF NOT EXISTS idx_domain_events_operation_id ON public.domain_events (operation_id);
CREATE INDEX IF NOT EXISTS idx_domain_events_event_type ON public.domain_events (event_type);
CREATE INDEX IF NOT EXISTS idx_domain_events_severity ON public.domain_events (severity);
CREATE INDEX IF NOT EXISTS idx_domain_events_entity_type_entity_id ON public.domain_events (entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_domain_events_created_at ON public.domain_events (created_at);
