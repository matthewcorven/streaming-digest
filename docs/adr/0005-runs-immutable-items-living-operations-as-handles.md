# Ingestion Runs are immutable, Items are living, Operations are request handles

The model has three nested tracking concepts (`operations`, `ingestion_runs`, `ingestion_items`) with no stated rules for what gets rewritten when a user retries a failed stage. Unclear ownership would let the run record, item rows, and operation records drift into contradiction.

We decided the three have distinct mutability contracts:

- **Ingestion Runs are immutable once `completed_at` is set** — summary counts and final status are frozen. Run detail pages derive a live rollup from current item states instead of re-reading the frozen record.
- **Ingestion Items are living rows** — retry mutates status/attempt/timestamps in place, and each retry writes a domain event so the timeline shows full history. Items stay attached to their originating run forever.
- **Operations are the tracking handle** — one per user/API request; a batch retry is one operation spanning many items; each item links only its latest operation.

## Consequences

- A run can show `failed` historically while its live rollup shows all-green after successful retries — this is intended, and UI copy should distinguish "run outcome" from "current state."
- Video Health (CONTEXT.md) reads item reality, never the frozen run record.
- `ingestion_runs` gains no "reopened" status; history questions go to domain events.
- API docs should state that `/api/ingestion/runs/{id}` returns the frozen record plus a derived live rollup, so consumers don't assume the summary counts are current.
