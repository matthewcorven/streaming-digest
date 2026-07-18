# Single active embedding model with a declared transition state

Switching the embedding model invalidates all existing embeddings (per ADR-0001's model-mismatch staleness rule), and regeneration at MVP corpus size can take tens of minutes. During that window, queries embedded with the new model would silently compare against old-model vectors — mathematically meaningless — unless the system takes a stance.

We considered dual-model querying (keep both models searchable during transition) and rejected it as complexity without MVP-scale payoff. We decided: one global Active Embedding Model pointer flips immediately on user confirmation, and the system enters a derived Embedding Transition state until the bulk regeneration operation completes.

## Consequences

- During transition, vector search covers only new-model embeddings and shrinks as old ones remain stale — the UI must show a "search coverage rebuilding" banner with progress rather than silently returning sparse results. Text search is unaffected, so hybrid search degrades gracefully.
- High-Signal Match evaluation is skipped for ingestion runs completing mid-transition (query embeddings and content fingerprints may not share a space).
- Transition is derived, not stored: active model ≠ model of the completed embedding generation ⇒ in transition. This is consistent with ADR-0001's no-stored-staleness stance.
- Scheduled ingestion during transition embeds with the new model — no special-casing.
- `API_SPEC.md` §5 (`activate-embedding-model`) and §13 (`embeddings/status`) should expose transition state and progress.
