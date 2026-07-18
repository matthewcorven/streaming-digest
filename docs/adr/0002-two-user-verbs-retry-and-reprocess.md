# Two user-facing verbs: Retry and Reprocess

The docs used three overlapping verbs for re-running work — retry, reprocess, and regenerate — with blurry boundaries (is "regenerate embeddings" a retry of one stage, or a distinct operation? can you retry a successful video?).

We decided there are exactly two user-facing verbs:

- **Retry** re-executes work in a failed/deferred state only. It never applies to succeeded work and is always idempotent.
- **Reprocess** re-executes the full pipeline for an already-succeeded entity, explicitly bypassing the idempotency/skip guard. Staleness and embedding regeneration follow as downstream consequences (per ADR-0001), so users never request "regenerate embeddings" directly — the sole exception is the bulk model-change flow, which is internally a reprocess of embeddings only.

## Consequences

- "Regenerate" disappears as a user-facing verb and survives only as internal job naming.
- API surface to rename/merge: `/api/batch/regenerate` folds into batch retry/reprocess semantics; operation type `regenerate_embeddings` is renamed (e.g. `reprocess_embeddings`) and reserved for the model-change flow; "regenerate item embeddings" admin actions become "reprocess item."
- Removes the ambiguity of whether reprocessing re-embeds (it does, by construction) and whether retrying a failed embedding differs from regenerating it (it doesn't).
- `API_SPEC.md` §2.5, §13, §16 and `IMPLEMENTATION_PLAN.md` Phase 16 admin actions should be updated to match.
