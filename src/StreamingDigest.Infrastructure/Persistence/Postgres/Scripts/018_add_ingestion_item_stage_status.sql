-- Migration: Add per-stage status tracking to ingestion_items
-- Purpose: Enable granular tracking of each pipeline stage (transcript, segments, screenshots, links, repos, websites, embeddings)
-- for per-video items within an ingestion run, supporting detail view, retry targeting, and orchestrator state management.

ALTER TABLE public.ingestion_items
ADD COLUMN IF NOT EXISTS transcript_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS segments_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS screenshots_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS links_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS repos_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS websites_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS embeddings_status text NOT NULL DEFAULT 'pending',
ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- Create indexes for querying items by stage status (common queries in orchestrator and admin ops)
CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_transcript_status
ON public.ingestion_items(ingestion_run_id, transcript_status)
WHERE transcript_status != 'completed';

CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_segments_status
ON public.ingestion_items(ingestion_run_id, segments_status)
WHERE segments_status != 'completed';

CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_screenshots_status
ON public.ingestion_items(ingestion_run_id, screenshots_status)
WHERE screenshots_status != 'completed';

CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_links_status
ON public.ingestion_items(ingestion_run_id, links_status)
WHERE links_status != 'completed';

CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_repos_status
ON public.ingestion_items(ingestion_run_id, repos_status)
WHERE repos_status != 'completed';

CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_websites_status
ON public.ingestion_items(ingestion_run_id, websites_status)
WHERE websites_status != 'completed';

CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_embeddings_status
ON public.ingestion_items(ingestion_run_id, embeddings_status)
WHERE embeddings_status != 'completed';

-- Index for querying by run_id and any failed status (used for retry targeting)
CREATE INDEX IF NOT EXISTS idx_ingestion_items_run_failed_stages
ON public.ingestion_items(ingestion_run_id)
WHERE transcript_status = 'failed'
   OR segments_status = 'failed'
   OR screenshots_status = 'failed'
   OR links_status = 'failed'
   OR repos_status = 'failed'
   OR websites_status = 'failed'
   OR embeddings_status = 'failed';
