CREATE TABLE IF NOT EXISTS public.notes (
    id uuid PRIMARY KEY,
    target_type text NOT NULL,
    target_id uuid NOT NULL,
    title text NULL,
    markdown text NOT NULL,
    embedding_status text NOT NULL DEFAULT 'stale',
    deleted_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- MVP: one live note per target (target_type, target_id) WHERE deleted_at IS NULL
CREATE UNIQUE INDEX IF NOT EXISTS idx_notes_target_unique_live
    ON public.notes (target_type, target_id)
    WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_notes_embedding_status
    ON public.notes (embedding_status);
