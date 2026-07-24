### Task 11.4: Store embeddings in pgvector

Source: `docs/architecture/DATA_MODEL.md` §3.22

Requirements:

- content hash.
- provider/model/dimensions.
- idempotent regeneration.

Verification:

- Re-running embedding job does not duplicate unchanged embeddings.

