### Task 15.3: Store domain events and warning/error summaries

Source: `docs/architecture/DATA_MODEL.md` §3.31

Do not store every log line in Postgres.

Verification:

- Failed scrape creates domain event and Loki log.

