# Reprocess eligibility is pipeline-completion, and Reprocess bypasses scrape-exclusion skips

Two gaps in the Retry/Reprocess vocabulary (ADR-0002) left entities strand-able. First, the Retry Budget (DATA_MODEL §3.29) says exhaustion is recoverable by Reprocess — but Reprocess was defined for "already-succeeded" entities, leaving it ambiguous whether a `processed_with_warnings` video with a budget-exhausted enrichment stage qualifies; if not, the budget is a one-way valve that strands enrichment data forever. Second, scrape exclusions ("skip that URL unless the URL value changes") are policy outcomes, not failures, so Retry correctly ignores them — but that made an exclusion a permanent, invisible veto even when the world changed (a login-walled site that later goes public).

We decided:

- Reprocess eligibility means the entity's pipeline completed (any ingestion status other than Core-Stage `failed`, including `processed_with_warnings`). Reprocessing resets Retry Budgets per ADR-0002's "deliberate fresh start."
- Reprocess re-evaluates scrape-exclusion policy against the live site; Retry does not. The original exclusion record is retained as history, and the resource detail page shows exclusion history so the veto is never invisible.

## Considered options

- Budget exhaustion terminal for the entity: harshest reading, with no recovery path short of delete-and-re-ingest.
- Exclusion-skip binds Reprocess too: recovery would require editing the URL (e.g., a trailing slash) as a workaround — an undiscoverable hack.
- Time-limited exclusions (TTL with automatic re-attempt): adds a clock to a policy rule and re-attempts scrapes the user may know are pointless; Reprocess-as-reconsideration covers the real need with an explicit user gesture.

## Consequences

- API_SPEC's Reprocess endpoints (videos, repositories, external resources) accept any completed entity; stage-level budget exhaustion no longer blocks them.
- The website_scrape stage consults exclusion history only on normal ingestion and Retry paths; the Reprocess path bypasses the skip and writes a fresh outcome (success or a new exclusion reason).
- UI copy: the budget-exhausted state should name Reprocess as the recovery path ("permanently failed — Reprocess to start fresh").
- ADR-0002's vocabulary stands unchanged; this ADR clarifies applicability rather than adding a third verb.
