### Task 4.2: Implement ingestion run records

Source: `docs/architecture/DATA_MODEL.md` §3.28–3.29; ADR-0005

Support:

- scheduled run.
- manual run.
- backfill run.
- per-item statuses.

Verification:

- Starting manual run creates `ingestion_runs` and `ingestion_items`.

