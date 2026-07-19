# Scheduled ingestion pauses during Embedding Transition, with catch-up on completion

ADR-0008 declared that scheduled ingestion during an Embedding Transition embeds with the new model — "no special-casing." In practice the transition can last tens of minutes to hours, a scheduled run will frequently land mid-transition, and its Digest would either silently lose High-Signal evaluation or emit a degraded artifact. We decided the daily run treats a coherent corpus as more valuable than a punctual one.

We decided:

- Scheduled runs pause while an Embedding Transition is in progress; manual runs may proceed (the user is present and can see the coverage banner).
- One catch-up `scheduled` run fires immediately when the transition completes, so the ingestion outage never exceeds the transition window and the missed digest is restored.
- Transition completes when the bulk regeneration Operation completes, regardless of per-item failures: permanently-failed embeddings degrade to ordinary stale-embedding pending-action inbox items, and the banner's coverage percent counts only processable documents so it can reach 100%.
- The catch-up run's Digest backfills High-Signal evaluation once, over all videos ingested during the transition (by any manual runs), so the subscription signal survives model changes. Mid-transition run Digests remain immutable per ADR-0005.

## Considered options

- Allow scheduled runs mid-transition (ADR-0008 status quo): new videos searchable immediately, but digests silently degrade and High-Signal evaluation is skipped with no trace.
- Allow runs but defer Digest assembly until transition completes: keeps ingestion moving, but delays the digest arbitrarily and complicates the "assembled once at run completion" rule.
- Require 100% regeneration success before completion: a handful of poisoned documents would hold the entire ingestion pipeline hostage indefinitely.

## Consequences

- The scheduler must check derived transition state (active model ≠ completed generation's model) before firing a scheduled run; manual-run endpoints do not check.
- The transition-completion path must enqueue exactly one catch-up run (guard against double-fire if completion and the schedule coincide).
- `ingestion_runs` for the catch-up run is a normal `scheduled` run; its summary may note the post-transition trigger for diagnostics, but run types are not extended.
- Digest assembly for the catch-up run queries videos ingested since transition start that were never High-Signal-evaluated, and folds them into its High-Signal section.
- UI copy: the coverage banner should mention that scheduled ingestion is paused and will catch up automatically, so a missing 6 AM run is explainable without reading logs.
