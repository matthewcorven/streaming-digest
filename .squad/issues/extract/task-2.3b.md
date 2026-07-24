### Task 2.3b: Scaffold API contract conformance harness

Scaffold the Phase 18 conformance test early so contract drift is detected per phase rather than only at the end:

- Generate/enumerate the MVP endpoint catalog from `docs/api/API_SPEC.md`.
- Each catalog entry asserts route existence and auth behavior; unimplemented routes are tracked as a known-pending list that shrinks as phases complete.
- The known-pending list must be empty for Phase 18 to pass.

Verification:

- Harness runs in CI from Phase 2 onward and reports implemented-vs-pending endpoint counts.

