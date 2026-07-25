# Phase-gate Task X.0 references denote previous-slice completion, not gated launches

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

We decided: **a bare `Task X.0` reference means "the work before this head is complete," and is
rewritten to the last real task that must finish before the head starts — where "before" is
defined by the plan's vertical-slice execution order, not by numeric phase order.** The rewrite
is a one-time, reversible data fix on the 17 issue bodies; the convention going forward is that
new chains reference the real last task of the preceding slice, and no `X.0` placeholder ref is
ever introduced again.

Evidence that execution order is slices, not phases: the plan's own
`## Implementation sequencing` section states (line 1743) *"Execution order is the **vertical
slices** below; phase numbering is a reference grouping for requirements, **not the build
order**."* Execution order is the numbered vertical slices (lines 1745-1759), which interleave
phases; the `phase-N` label is pure grouping metadata. A phase is therefore not a launch gate a
human opens; it is a numbering boundary.

**This ADR originally encoded the dependency graph as numeric *phase* order, contradicting the
very quote above. That is corrected here: the convention is previous-*slice* completion.** In 13
of the 17 cases numeric phase order and slice order coincide (the preceding slice ends with the
preceding numeric phase's last task), so those edges are identical under either reading. In 4
cases the slice list runs a later-numbered phase *before* an earlier-numbered one, so the
numeric-phase edge pointed at a task scheduled *after* the head — an inversion that would, for
example, block the M1 killer-journey checkpoint (slices 1-5) behind website scraping (slice 8)
and behind 7 later slices (slice 12). Those 4 are re-pointed to the last task of the preceding
*slice*.

The rewrite map (old bare ref → new ref → new referent issue). Every edge points at the last
task of the **preceding slice** (never the head's own phase — a same-phase edge would be a
circular dependency). The 4 slice-order corrections are marked †:

| Head issue | Old ref | New ref | New referent | Notes |
|---|---|---|---|---|
| #6 [Task 1.1] | 1.0 | 0.5 | #5 | |
| #54 [Task 2.1] | 2.0 | 1.5 | #10 | |
| #63 [Task 3.1] | 3.0 | 2.6 | #62 | |
| #65 [Task 4.1] | 4.0 | 3.2 | #64 | |
| #71 [Task 5.1] | 5.0 | 4.6 | #70 | |
| #76 [Task 6.1] | 6.0 | 5.5 | #75 | |
| #80 [Task 7.1] | 7.0 | 4.6 | #70 | † slice 5→3 (was 6.4/#79, slice 10) |
| #86 [Task 8.1] | 8.0 | 7.5 | #85 | |
| #91 [Task 9.1] | 9.0 | 8.5 | #90 | |
| #11 [Task 10.1] | 10.0 | 9.4 | #94 | |
| #15 [Task 11.1] | 11.0 | 5.5 | #75 | † slice 5→4 (was 10.3/#14, slice 8) |
| #24 [Task 12.1] | 12.0 | 5.5 | #75 | † slice 5→4 (was 11.7/#23, slice 12) |
| #33 [Task 13.1] | 13.0 | 8.4 | #89 | † slice 11→8 (was 12.8/#32, slice 12) |
| #36 [Task 14.1] | 14.0 | 13.3 | #35 | |
| #40 [Task 15.1] | 15.0 | 14.4 | #39 | |
| #45 [Task 16.1] | 16.0 | 15.5 | #44 | |
| #48 [Task 17.1] | 17.0 | 16.3 | #47 | |

Slice derivation for the 4 corrections (positional numbering; the plan has a duplicate "5" typo,
so slices are counted by position):
- **#15 / #24** are in slice 5 ("Phases 6, 11, early 12", the M1 checkpoint). The preceding slice
  is slice 4 ("Basic yt-dlp metadata ingestion, Phase 5"), whose last task is 5.5 (#75).
  "Early 12" is taken to mean the search-readiness subset (12.1-12.4) that sits in slice 5, so
  12.1's only outstanding predecessor outside the slice is slice 4 → 5.5 (#75).
- **#80** is in slice 6 ("Segmentation + screenshots, Phase 7"). The preceding slice is slice 3
  ("Phases 2-4"), whose last task is 4.6 (#70).
- **#33** is in slice 11 ("Notes/edit/re-embedding, Phase 13"). The preceding slice is slice 8
  ("Local LLM classification/semantic segmentation, 8.4 + 7.3"), whose last task is 8.4 (#89).

After the rewrite, a full `--state all` scan reports zero missing referents, and all 17 heads
remain correctly blocked behind a real OPEN issue. The 4 re-points changed **zero** availability
— every re-pointed head is still blocked behind a real OPEN issue (#75, #70, #89).

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
  without releasing the backlog. The target is the last task of the **preceding vertical
  slice**, not the preceding numeric phase — the original framing ("previous phase's last
  task") was refined after review because 4 edges inverted the slice order (see table).

## Consequences

- The 17 chain heads now resolve their dependencies against real issues and flow through the
  ready-vs-blocked queue correctly; none is available until its preceding-slice last task closes.
- The M1 killer-journey checkpoint (slices 1-5) is no longer transitively blocked behind slice 8
  (website scraping) or slice 12 — #15 and #24 now wait only on slice 4's last task (#75),
  consistent with the plan's "validates the killer journey as early as slice 5."
- The queue helper's `(missing)` marker makes the entire class of "reference to a nonexistent
  issue" defect visible in text output forever — a missing referent can never again masquerade
  as a routine open dependency.
- Going forward, a new chain's first task must reference the actual last task of the **preceding
  slice** in its `## Depends On` section; bare `X.0` refs are not used.
- If a genuine human-opened phase gate is ever wanted, it is created as a real issue and
  referenced by number — never as a bare `X.0` placeholder.
- This ADR is a process/convention ruling on tracker data, recorded as an ADR (not merely a
  verification note) because it sets a convention other agents must follow when authoring or
  editing issue dependencies. Evidence is in `docs/verification/101-issue-queue-readiness.*`.
