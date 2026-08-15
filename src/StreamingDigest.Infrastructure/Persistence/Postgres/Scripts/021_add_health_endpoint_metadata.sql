-- Migration 021: Add health endpoint metadata tables for live service health probes
-- Purpose: Persistent backup metadata tracking + ephemeral service health cache for GET /api/admin/health

-- backup_metadata table: persistent backup verification record
CREATE TABLE IF NOT EXISTS backup_metadata (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    location TEXT NOT NULL,
    verified_at TIMESTAMPTZ,
    health_status TEXT NOT NULL DEFAULT 'pending',
    details_json JSONB,
    CONSTRAINT chk_backup_health_status CHECK (health_status IN ('pending', 'verified', 'failed', 'expired'))
);

-- service_health table: live service probe results (ephemeral cache)
CREATE TABLE IF NOT EXISTS service_health (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    service_name TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL,
    is_required BOOLEAN NOT NULL DEFAULT true,
    last_check TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    latency_ms INTEGER,
    details_json JSONB,
    retry_count INTEGER DEFAULT 0,
    CONSTRAINT chk_service_status CHECK (status IN ('Ready', 'Degraded', 'Reconnecting', 'Error', 'Paused'))
);

-- stale_data_audit view: aggregate stale data signals from search, embeddings, and segments
CREATE OR REPLACE VIEW stale_data_audit AS
SELECT 
    COALESCE(COUNT(sd.id) FILTER (WHERE sd.is_stale = true), 0)::INT as stale_search_documents,
    COALESCE(COUNT(DISTINCT e.search_document_id) FILTER (WHERE e.embedding_status = 'pending'), 0)::INT as pending_embeddings,
    COALESCE(COUNT(s.id) FILTER (WHERE s.requires_user_approval = true), 0)::INT as segments_pending_approval,
    COALESCE(COUNT(s.id) FILTER (WHERE s.requires_embedding_approval = true), 0)::INT as segments_pending_embedding,
    CURRENT_TIMESTAMP as audited_at
FROM search_documents sd
FULL OUTER JOIN embeddings e ON sd.id = e.search_document_id
FULL OUTER JOIN segments s ON true;

-- Indexes for efficient queries
CREATE INDEX IF NOT EXISTS idx_backup_metadata_verified_at ON backup_metadata(verified_at);
CREATE INDEX IF NOT EXISTS idx_backup_metadata_created_at ON backup_metadata(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_service_health_service_name ON service_health(service_name);
CREATE INDEX IF NOT EXISTS idx_service_health_status ON service_health(status);
CREATE INDEX IF NOT EXISTS idx_search_documents_is_stale ON search_documents(is_stale) WHERE is_stale = true;
CREATE INDEX IF NOT EXISTS idx_embeddings_status ON embeddings(embedding_status) WHERE embedding_status = 'pending';
CREATE INDEX IF NOT EXISTS idx_segments_approvals ON segments(requires_user_approval, requires_embedding_approval) WHERE requires_user_approval = true OR requires_embedding_approval = true;
