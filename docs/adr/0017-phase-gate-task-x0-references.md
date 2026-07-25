# Phase-gate Task X.0 references denote previous-phase completion, not gated launches

The issue tracker (authoritative for execution ordering, per `.github/copilot-instructions.md`)
contained 17 phase-chain-head issues whose `## Depends On` section referenced a bare task id
like `1.0`, `10.0`, `12.0`. No issue exists with a `[Task X.0]` title in any state, and the
retired `docs/implementation/IMPLEMENTATION_PLAN.md` (git history, commit `a8d57fe^`) contains
**zero** `### Task X.0:` headings — phases were `## Phase N:` headers, never tasks. The refs
were migration artifacts: in the plan, a phase had no body of its own, so the first task of
each phase was written as depending on the phase itself.

Left as-is, those 17 heads were blocked forever under any flag — the referent could never
exist. Mass-unblocking them was explicitly rejected (it would release the entire backlog at
once, far more damaging than the under-reporting it fixed).

We decided: **a bare `Task X.0` reference means "the previous phase's work is complete," and
is rewritten to that phase's last task (in plan order).** The rewrite is a one-time,
reversible data fix on the 17 issue bodies; the convention going forward is that new phase
chains reference the real previous-phase last task, and no `X.0` placeholder ref is ever
introduced again.

Evidence that phases are reference groupings, not execution gates: the plan's own
`## Implementation sequencing` section states *"phase numbering is a reference grouping for
requirements, not the build order"* — execution order is the numbered vertical slices, and the
`phase-N` label is pure grouping metadata. A phase is therefore not a launch gate a human
opens; it is a numbering boundary. Encoding "previous phase complete" as a dependency on its
last real task preserves the plan's intent (a phase's first task does not start before the
prior phase's last task finishes) without inventing gate issues.

The rewrite map (old bare ref → new ref → new referent issue):

| Head issue | Old ref | New ref | New referent |
|---|---|---|---|
| #6 [Task 1.1] | 1.0 | 0.5 | #5 |
| #54 [Task 2.1] | 2.0 | 1.5 | #10 |
| #63 [Task 3.1] | 3.0 | 2.6 | #62 |
| #65 [Task 4.1] | 4.0 | 4.6 | #70 |
| #71 [Task 5.1] | 5.0 | 5.5 | #75 |
| #76 [Task 6.1] | 6.0 | 6.4 | #79 |
| #80 [Task 7.1] | 7.0 | 7.5 | #85 |
| #86 [Task 8.1] | 8.0 | 8.5 | #90 |
| #91 [Task 9.1] | 9.0 | 9.4 | #94 |
| #11 [Task 10.1] | 10.0 | 10.3 | #14 |
| #15 [Task 11.1] | 11.0 | 11.7 | #23 |
| #24 [Task 12.1] | 12.0 | 12.8 | #32 |
| #33 [Task 13.1] | 13.0 | 13.3 | #35 |
| #36 [Task 14.1] | 14.0 | 14.4 | #39 |
| #40 [Task 15.1] | 15.0 | 15.5 | #44 |
| #45 [Task 16.1] | 16.0 | 16.3 | #47 |
| #48 [Task 17.1] | 17.0 | 16.3 | #47 |

After the rewrite, a full `--state all` scan reports zero missing referents, and all 17 heads
remain correctly blocked behind a real OPEN issue.

## Considered options

- **(a) Phase gates are intentional human-opened launches** — rejected: contradicted by the
  plan, which contains no `Task X.0` entries and declares phases "a reference grouping, not
  the build order." There is no gate to open; the blocked display was a defect, not a design.
- **(b) Create real `[Task X.0]` phase-gate issues** — rejected: invents 17 empty issues whose
  only purpose is to satisfy a broken reference. It adds tracker noise, gives Ralph 17 fake
  board items, and encodes a gate concept the plan explicitly disclaims.
- **(c) Treat an unresolvable ref as satisfied-with-warning** — rejected as the *primary*
  mechanism: it would mass-unblock all 17 at once, the exact outcome the ruling forbids.
  Adopted only as a **safety net**: the helper now renders a missing referent as
  `ref (missing)` so any *future* broken ref is visibly distinct from an open referent and
  cannot hide. Missing refs still block; they are just no longer invisible.
- **(d) Rewrite the 17 bodies to reference real issues** — **chosen**: least-destructive,
  reversible, preserves the plan's true ordering semantics, and fixes the under-reporting
  without releasing the backlog.

## Consequences

- The 17 chain heads now resolve their dependencies against real issues and flow through the
  ready-vs-blocked queue correctly; none is available until its previous-phase last task closes.
- The queue helper's `(missing)` marker makes the entire class of "reference to a nonexistent
  issue" defect visible in text output forever — a missing referent can never again masquerade
  as a routine open dependency.
- Going forward, a new phase chain's first task must reference the previous phase's actual last
  task in its `## Depends On` section; bare `X.0` refs are not used.
- If a genuine human-opened phase gate is ever wanted, it is created as a real issue and
  referenced by number — never as a bare `X.0` placeholder.
- This ADR is a process/convention ruling on tracker data, recorded as an ADR (not merely a
  verification note) because it sets a convention other agents must follow when authoring or
  editing issue dependencies. Evidence is in `docs/verification/101-issue-queue-readiness.*`.
