CREATE TABLE IF NOT EXISTS public.notifications (
    id uuid PRIMARY KEY,
    operation_id uuid,
    ingestion_run_id uuid,
    notification_type text NOT NULL,
    provider text NOT NULL,
    target text NOT NULL,
    status text NOT NULL,
    payload_json jsonb,
    rendered_body text,
    message_summary text,
    provider_message_id text,
    attempt_count integer NOT NULL DEFAULT 0,
    next_retry_at timestamptz,
    error_summary text,
    sent_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_notifications_ingestion_run_id ON public.notifications (ingestion_run_id);
CREATE INDEX IF NOT EXISTS idx_notifications_operation_id ON public.notifications (operation_id);
CREATE INDEX IF NOT EXISTS idx_notifications_status ON public.notifications (status);

CREATE TABLE IF NOT EXISTS public.outbox_messages (
    id uuid PRIMARY KEY,
    message_type text NOT NULL,
    aggregate_type text,
    aggregate_id uuid,
    payload_json jsonb NOT NULL,
    status text NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    next_attempt_at timestamptz,
    last_error_summary text,
    sent_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_outbox_messages_status ON public.outbox_messages (status);
CREATE INDEX IF NOT EXISTS idx_outbox_messages_next_attempt_at ON public.outbox_messages (next_attempt_at);
