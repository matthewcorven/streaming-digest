### Task 11.5: Implement stale embedding regeneration

Source: `docs/architecture/DATA_MODEL.md` §7; ADR-0001 (staleness is derived — compute via hash/model comparison, never write a flag)

Triggers:

- override edit.
- note edit.
- model change.
- failed embedding retry.

Verification:

- Editing a title marks document stale and regeneration clears stale flag.

