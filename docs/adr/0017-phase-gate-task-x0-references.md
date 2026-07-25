# Phase-gate Task X.0 references denote real upstream data/capability dependencies

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

We decided: **a bare `Task X.0` reference is rewritten to the real issue whose completion
produces the data or capability the head actually consumes.** Where that coincides with the
previous phase's last task, the edge is unremarkable. Where it does not, the ADR records why.
If no upstream issue exists, the head has **no dependency**. Numeric adjacency is never
sufficient by itself.

Evidence that phases are reference groupings, not execution gates: the plan's own
`## Implementation sequencing` section states *"phase numbering is a reference grouping for
requirements, not the build order"* — execution order is the numbered vertical slices, and the
`phase-N` label is pure grouping metadata. A phase is therefore not a launch gate a human
opens; it is a numbering boundary. Slice order remains useful corroborating evidence, but it is
not the governing rule. The governing rule is the task's **real upstream dependency**.

### Convention refinement (2026-07-25 follow-up): real dependency, not numeric or slice adjacency

The earlier follow-up draft corrected "previous phase" to "previous slice." That still encoded
adjacency as the rule, and only used real semantics in the exception notes. The user ruled that
the honest convention is: **rewrite the head to the issue that produces the data or capability
it actually consumes**. Slice order is still a useful consistency check, but it is not the
decision procedure. Every retained edge must still point **backward** in execution order; an
edge is never justified by mere numeric adjacency.

For **12 of the 17** heads, the honest real dependency still coincides with the previous
numeric phase's last task. The five divergences are `#15`, `#24`, `#33`, `#36`, and `#80`.
Those are recorded explicitly below.

The rewrite map below records the **live** issue bodies (verified against GitHub after the
one-time rewrite; see `docs/verification/101-issue-queue-readiness.md`). It is regenerated from
live state after any body edit — never hand-carried forward from an intermediate map.

| Head issue | Old ref | New ref | New referent |
|---|---|---|---|
| #6 [Task 1.1] | 1.0 | 0.5 | #5 |
| #54 [Task 2.1] | 2.0 | 1.5 | #10 |
| #63 [Task 3.1] | 3.0 | 2.6 | #62 |
| #65 [Task 4.1] | 4.0 | 3.2 | #64 |
| #71 [Task 5.1] | 5.0 | 4.6 | #70 |
| #76 [Task 6.1] | 6.0 | 5.5 | #75 |
| #80 [Task 7.1] | 7.0 | 5.5 | #75 |
| #86 [Task 8.1] | 8.0 | 7.5 | #85 |
| #91 [Task 9.1] | 9.0 | 8.5 | #90 |
| #11 [Task 10.1] | 10.0 | 9.4 | #94 |
| #15 [Task 11.1] | 11.0 | 5.5 | #75 |
| #24 [Task 12.1] | 12.0 | 11.2 | #16 |
| #33 [Task 13.1] | 13.0 | 11.1 | #15 |
| #36 [Task 14.1] | 14.0 | None | None |
| #40 [Task 15.1] | 15.0 | 14.4 | #39 |
| #45 [Task 16.1] | 16.0 | 15.5 | #44 |
| #48 [Task 17.1] | 17.0 | 16.3 | #47 |

After the rewrite, a full `--state all` scan reports zero missing referents, and all 17 heads
remain correctly resolved: 16 heads point at a real issue, and `#36` honestly has no upstream
issue dependency.

### Non-obvious edges and divergences from previous-phase adjacency

- **#15 [Task 11.1] → 5.5 / #75 (confirmed as-is).** The effective-value service's first
  concrete inputs are the editable scraped video fields produced by Phase 5 ingestion. Later
  override families (transcripts, links, repositories) reuse that abstraction, but do not block
  defining it.
- **#24 [Task 12.1] → 11.2 / #16 (changed from `5.5 / #75`).** The task body says
  *"Search over `search_documents`."* The real prerequisite is the search-document generator,
  not merely raw ingested video metadata. This overturns the earlier "Phase 5 is enough"
  reading.
- **#33 [Task 13.1] → 11.1 / #15 (changed from `8.4 / #89`).** Override APIs are built on the
  `original` / `override` / `effective` contract and `field_override_history`. The enabling
  capability is the effective-value service, not local LLM classification. `8.4` remains an
  input to one override subtype, not the general gate for the override API surface.
- **#36 [Task 14.1] → no dependency (changed from `13.3 / #35`).** Selecting a Matrix SDK is a
  research/evaluation task. It does not consume the edit-modal work from Phase 13, and it does
  not need the stored Digest artifact — that dependency belongs downstream in the notification
  implementation path if anywhere. Under the real-dependency rule, inventing an adjacency blocker
  here would be less honest than leaving the issue dependency-free.
- **#80 [Task 7.1] → 5.5 / #75 (changed from `4.6 / #70`).** Author chapters come from the
  yt-dlp metadata fetched in Phase 5. Hangfire concurrency tests produce nothing Task 7.1
  consumes.

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
  without inventing fake gate issues.

## Consequences

- The 17 chain heads now resolve against the real data/capability they consume. For 16 heads
  that is a real upstream issue; for `#36` it is honestly no dependency.
- The queue helper's `(missing)` marker makes the entire class of "reference to a nonexistent
  issue" defect visible in text output forever — a missing referent can never again masquerade
  as a routine open dependency.
- Going forward, a new phase chain's first task must reference the issue that produces the data
  or capability it actually consumes. Bare `X.0` refs are not used, and "previous phase" is not
  the default answer unless it is also the real dependency.
- **Convention clarification (2026-07-25):** 12 of the 17 heads happen to coincide with the
  previous numeric phase's last task; five do not (`#15`, `#24`, `#33`, `#36`, `#80`). Slice
  order is corroborating evidence only. The governing rule is real dependency.
- If no real upstream issue exists, leave `## Depends On` empty / `None`; do not invent an
  adjacency gate solely to keep the item blocked.
- If a genuine human-opened phase gate is ever wanted, it is created as a real issue and
  referenced by number — never as a bare `X.0` placeholder.
- This ADR is a process/convention ruling on tracker data, recorded as an ADR (not merely a
  verification note) because it sets a convention other agents must follow when authoring or
  editing issue dependencies. Evidence is in `docs/verification/101-issue-queue-readiness.*`.
