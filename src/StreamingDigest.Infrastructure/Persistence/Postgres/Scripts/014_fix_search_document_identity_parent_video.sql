DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'uq_search_documents_identity'
          AND conrelid = 'public.search_documents'::regclass
    ) THEN
        ALTER TABLE public.search_documents
            DROP CONSTRAINT uq_search_documents_identity;
    END IF;
END $$;

ALTER TABLE public.search_documents
    ADD CONSTRAINT uq_search_documents_identity UNIQUE NULLS NOT DISTINCT (
        parent_video_id,
        source_entity_type,
        source_entity_id,
        source_field_name,
        chunk_index,
        content_hash
    );
