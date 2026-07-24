### Task 4.6: Implement concurrency and race conformance tests

Source: `docs/architecture/DATA_MODEL.md` §3.9, §3.21, §3.29, §3.34; ADR-0005

Requirements:

- Concurrency tests for the race-prone invariants: parallel retries of the same ingestion item; concurrent segment-generation approvals against the single-active-generation partial unique index; the one-active-transcript invariant under concurrent cutovers (Task 6.1); digest assembly when two runs complete near-simultaneously (Task 12.5a); outbox double-dispatch under concurrent workers (Task 14.4).
- This task delivers the test harness plus the retry and unique-index scenarios; scenario tests for later features land alongside those features.

Verification:

- Each scenario above has a failing-before/passing-after test demonstrating the invariant holds under concurrency.

## Phase 5: YouTube ingestion adapter

