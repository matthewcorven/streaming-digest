# Spikes — throwaway prototype code

Everything under `spikes/` is **throwaway prototype code**. It is not production code and
must never be treated as such.

## Convention

- **Not in the solution.** Spike projects are deliberately excluded from
  `StreamingDigest.slnx` and have zero references from `src/` or `tests/`. They cannot
  affect a solution build, and there is no spike-specific build to exclude from CI.
- **Kept on `main` as primary-source evidence.** Each spike is the re-runnable evidence for
  its committed `docs/verification/<task>-*.md` report. Deleting a spike orphans the report's
  "Re-run" section. Keep the code with the report.
- **Decisions graduate; code does not.** When a prototype validates a decision, the decision
  is recorded (verification report, and an ADR when the ADR bar is met) and re-implemented
  properly in `src/`. The spike code itself is never copied into production.
- **One spike per prototype task**, named `spikes/StreamingDigest.<Name>Prototype/`, run
  against the real stack per the synthetic-data prototype policy.

## Current spikes

| Spike | Task | Report | PR |
|-------|------|--------|----|
| `StreamingDigest.VectorPrototype/` | 11.3a + 11.3b | `docs/verification/11.3a-vector-knowledge-base.md`, `11.3b-vector-user-search.md` | #96, #97 |
| `StreamingDigest.ScreenshotPrototype/` | 7.4 | `docs/verification/7.4-screenshot-extraction.md` | #98 |
