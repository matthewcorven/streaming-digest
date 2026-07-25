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
opens; it is a numbering boundary.

### Convention refinement (2026-07-25 follow-up): previous-**slice**, not previous-**phase**

The original text above quoted the plan's *"not the build order"* line and then encoded
**numeric phase order** anyway ("previous phase's last task"). That is a self-contradiction.
The plan's execution order is the **vertical slices**, not the numeric phases. The correct
convention is therefore: **a head's gate is completion of the *previous slice* in execution
order, which — for 13 of the 17 heads — coincides with the previous numeric phase, and for 4
does not.** Every rewrite edge points at the **previous slice's** last task (or the head's own
upstream within the same slice), **never forward into the head's own phase or a later slice.**

Where a head's previous slice coincides with the previous numeric phase, the edge is the
previous phase's last task. Where the plan defers tasks *out* of a phase into a later slice
(e.g. Whisper `6.3–6.4` deferred from Phase 6 into slice 9; `11.7` deferred from Phase 11 into
slice 12), or pulls a phase's first task *forward* into an earlier slice, the numeric phase is
**not** the build order and the edge must follow the slice, not the phase number.

The rewrite map below records the **live** issue bodies (verified against GitHub after the
one-time rewrite; see `docs/verification/101-issue-queue-readiness.md`). **Every edge points at
the previous phase's/slice's last task — never at a task inside the head's own phase.**

| Head issue | Old ref | New ref | New referent |
|---|---|---|---|
| #6 [Task 1.1] | 1.0 | 0.5 | #5 |
| #54 [Task 2.1] | 2.0 | 1.5 | #10 |
| #63 [Task 3.1] | 3.0 | 2.6 | #62 |
| #65 [Task 4.1] | 4.0 | 3.2 | #64 |
| #71 [Task 5.1] | 5.0 | 4.6 | #70 |
| #76 [Task 6.1] | 6.0 | 5.5 | #75 |
| #80 [Task 7.1] | 7.0 | 4.6 | #70 |
| #86 [Task 8.1] | 8.0 | 7.5 | #85 |
| #91 [Task 9.1] | 9.0 | 8.5 | #90 |
| #11 [Task 10.1] | 10.0 | 9.4 | #94 |
| #15 [Task 11.1] | 11.0 | 5.5 | #75 |
| #24 [Task 12.1] | 12.0 | 5.5 | #75 |
| #33 [Task 13.1] | 13.0 | 8.4 | #89 |
| #36 [Task 14.1] | 14.0 | 13.3 | #35 |
| #40 [Task 15.1] | 15.0 | 14.4 | #39 |
| #45 [Task 16.1] | 16.0 | 15.5 | #44 |
| #48 [Task 17.1] | 17.0 | 16.3 | #47 |

After the rewrite, a full `--state all` scan reports zero missing referents, and all 17 heads
remain correctly blocked behind a real OPEN issue.

### Slice-order exceptions — 4 edges flagged for a user scheduling ruling (NOT silently re-pointed)

A 2026-07-25 review flagged 4 live edges as candidates that may invert the plan's slice build
order. Re-pointing a dependency is a **scheduling decision** (it changes when work becomes
available), not a data cleanup. All 4 heads are currently Blocked in the live queue (none is
Available), so a re-point could only *change what they wait on*, not silently release them —
but choosing a different prerequisite still picks one build order over another, which is the
user's call. Per the ruling's hard constraints, these were **left as-is and flagged for a user
ruling** rather than guessed:

- **#15 [Task 11.1] (slice 5) → 5.5 / #75** — head is in slice 5 (Transcript + embeddings +
  basic search UI); current referent is Phase 5's last task (yt-dlp ingestion, slice 4).
  Numeric-phase order would point at 11.6, but Phase 11 is *inside* the same slice 5, so a
  phase-number edge would be a forward reference into the head's own slice/phase. Live value is
  slice-consistent. Candidate re-point (to an intra-slice-5 upstream) is a scheduling call.
- **#24 [Task 12.1] (slice 5) → 5.5 / #75** — same shape as #15; Phase 12 is split across
  slices 5/12, and 12.1 is in the slice-5 ("early 12") portion. Live value is slice-consistent.
- **#80 [Task 7.1] (slice 6) → 4.6 / #70** — head is in slice 6 (Segmentation + screenshots);
  current referent is Phase 4's last task (Hangfire, slice 3). A Phase-6 referent would be
  slice-inconsistent (Phase 6 is split across slices 5/9/10). Live value is slice-consistent.
- **#33 [Task 13.1] (slice 11) → 8.4 / #89** — head is in slice 11 (Notes/edit); current
  referent is 8.4 (#89, slice 9, Local LLM classification). Live value is slice-consistent.

**Status:** these 4 are **open questions for the user**. The live bodies are internally
consistent (no forward reference into the head's own phase, every referent is a real OPEN
issue, nothing is mass-unblocked, and — because all 4 are currently Blocked — no re-point
could silently release work into Available). Any change to them requires an explicit user
scheduling ruling and is tracked in `docs/verification/101-issue-queue-readiness.md`.

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
- **Convention clarification (2026-07-25):** the dependency target is the **previous slice's**
  last task in execution order, not the previous *numeric* phase's last task. For 13 of the 17
  heads these coincide; for the 4 flagged exceptions the distinction matters and any re-point
  is a user scheduling ruling, not a data fix. Every edge points backward in slice order,
  never forward into the head's own phase or a later slice.
- If a genuine human-opened phase gate is ever wanted, it is created as a real issue and
  referenced by number — never as a bare `X.0` placeholder.
- This ADR is a process/convention ruling on tracker data, recorded as an ADR (not merely a
  verification note) because it sets a convention other agents must follow when authoring or
  editing issue dependencies. Evidence is in `docs/verification/101-issue-queue-readiness.*`.
