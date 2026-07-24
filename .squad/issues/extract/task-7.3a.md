### Task 7.3a: Implement segment regeneration cutover

Source: `docs/architecture/DATA_MODEL.md` §3.9 (`segment_generations`); ADR-0001, ADR-0002

Requirements:

- Never re-segment during normal daily ingestion. Re-segmentation is explicit user action only, producing a new Segment Generation; approval performs the cutover: the new generation becomes Active, old-generation screenshots are purged, search documents and embeddings become stale by derivation and are reprocessed for the new segments, and notes on old-generation segments become Orphaned Notes surfaced in the pending-action inbox for re-anchor or delete.

Verification:

- Explicit re-segmentation stages embedding updates pending approval.
- Cutover integration test: approval activates the new generation, purges old screenshots, and surfaces an Orphaned Note in the inbox.

