-- WS-7 S1/S3: allow deferred embedding rows. When the embedding model is unready, the seam
-- persists the search document and records an embedding row with embedding_status = 'pending'
-- and no vector (NULL), so the document stays text-searchable and the embedding can be
-- backfilled once the model becomes ready. Relax the NOT NULL constraint introduced in 013.
ALTER TABLE public.embeddings
    ALTER COLUMN embedding DROP NOT NULL;
