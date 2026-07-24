### Task 1.1: Create PostgreSQL migration baseline

Implement schema from `docs/architecture/DATA_MODEL.md`.

Start with:

- `app_users`
- `app_settings`
- `channels`
- `videos`
- `ingestion_runs`
- `ingestion_items`
- `domain_events`

Then add content/search tables in later tasks.

Verification:

- Integration test applies migrations to test PostgreSQL.
- Required extensions installed: `vector`, `pg_trgm`, and `unaccent` when text-normalization search needs it per `docs/architecture/ARCHITECTURE.md`.

