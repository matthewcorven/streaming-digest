### Task 2.3a: Implement ingestion run detail UI

Build the ingestion run detail view required by `docs/product/PRD.md` §2.6:

- Timeline by stage, per-video status, and failures with retry buttons.
- Extracted links/repos/websites, transcript status, screenshot status, and embedding status per item.
- Links to Hangfire job, logs, and traces for the run.
- Prominent display of active rate-limit deferments for the run (Task 4.5).
- Runs are immutable once completed (ADR-0005): the page shows the frozen run outcome plus a live rollup derived from current item states, with copy distinguishing "run outcome" from "current state." Item timelines show retry history from domain events.

Verification:

- Fixture ingestion run renders stage timeline, per-item statuses, and retry actions.
- Deferment fixture renders active deferments prominently on the run detail page.
- A completed run with a subsequently retried-and-succeeded item renders frozen outcome and live rollup side by side.

