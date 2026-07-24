### Task 12.6: Implement dashboard daily digest and pending-action inbox

Source: `docs/product/PRD.md` §2.1, §2.10; ADR-0006

Requirements:

- Dashboard priority order is daily digest, search launchpad, then pending-action inbox.
- Daily digest shows new videos ingested, new repositories found, new websites/resources found, high-signal matches similar to recent searches, and failed/skipped items.
- The dashboard's digest section re-derives the active-deferments subsection from live state at render time (ADR-0006 amendment); all other digest fields render the stored artifact.
- Pending-action inbox orders pending approvals, failed ingestion, degraded channels (permanently-degraded items name their exit actions: Pause or delete, per ADR-0003 amendment), deferred rate limits, stale embeddings, model/service warnings, new digest items, recent-search matches, and storage/retention warnings.
- Post-login routing follows the product rule: incomplete onboarding, last selected mode, dashboard summary after first daily run, then ingestion/new-videos digest.
- Until the first run completes with at least one ingested video, the search page redirects to a pre-corpus waiting state with a run-now action; a zero-video first run keeps the waiting state with backfill guidance.

Verification:

- Fixture ingestion run renders digest sections in the correct priority order.
- High-signal recent-search matches appear with relative-similarity percentages and links to available timestamp/repository/website artifacts.
- Pending-action fixture renders retry/approve/test actions without requiring log inspection.

