### Task 1.5: Define domain event type vocabulary

Source: `docs/architecture/DATA_MODEL.md` §3.31

Requirements:

- Establish a single catalog of `event_type` values for `domain_events` (code-level enum/constants plus a docs listing), seeded with the event types implied by this plan: screenshot file missing (Task 7.5), transcript cutover override-inert count (6.1), scrape excluded/failed (10.3), rate-limit deferment created/expired/cleared (4.5), degraded channel entered/probe succeeded/probe failed (5.4), orphaned note surfaced (7.3a), temp-media orphan cleanup (6.4), embedding reprocess queued/completed/failed (11.5), notification dispatch outcome (14.4), digest assembled (12.5a).
- Convention: any task that writes a new kind of domain event must add its `event_type` to the catalog in the same change.

Verification:

- Catalog exists, is referenced by the domain-event writer, and contains at least the seeded types above.
- A convention test fails when code writes an `event_type` not present in the catalog.

## Phase 2: Authentication and app shell

