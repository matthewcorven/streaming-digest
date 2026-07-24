### Task 8.5: Classification correction workflow

Source: `docs/architecture/DATA_MODEL.md` §3.32; ADR-0007

When user edits classification:

- Store override.
- Store `classification_corrections`.
- Update rule/few-shot source.
- Mark relevant search documents stale.

Verification:

- Integration test correction changes future classification prompt examples.

## Phase 9: Repository ingestion

