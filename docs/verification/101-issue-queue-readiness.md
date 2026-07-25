# Verification: #101 — issue_queue.py readiness fix and Task X.0 phase-gate ruling

> Append-only evidence. Each run adds a dated entry; prior entries are never overwritten.

---

## Run 1 — 2026-07-25

### Outcome

**Bug confirmed and fixed.** `scripts/issue_queue.py` previously fetched only OPEN issues
by default, so any `## Depends On` / `## Blocked By` reference to a CLOSED issue resolved
to None and was scored as an unsatisfied dependency — a completed prerequisite was read as
a blocker, and the error silently worsened as more work completed. The fix changes the
`--state` default from `open` to `all` and adds a visible `(missing)` marker so a missing
referent is always distinguishable from an open referent in text output.

**Phase-gate ruling: option (d), with (c) as the safety net.** The 17 bare `X.0` refs were
rewritten to the real last task of the previous phase (evidence in ADR-0017); the helper's
new `(missing)` marker ensures any *future* unresolvable ref is visibly distinct. No issue
was mass-unblocked: each of the 17 now depends on a real OPEN issue and remains correctly
blocked until that prerequisite closes.

### Reproduction commands (exact)

```bash
# BEFORE the fix — documented invocation; #2 and #85 wrongly Blocked:
python3 scripts/issue_queue.py --repo matthewcorven/streaming-digest --limit 100 --format text
#   -> Next available: #12 ; Blocked includes "#2 ... (depends on 0.1)" and "#85 ... (depends on 7.4)"

# BEFORE the fix — forcing all states flips them (proof the referents are CLOSED, not absent):
python3 scripts/issue_queue.py --repo matthewcorven/streaming-digest --limit 100 --format text --state all
#   -> Next available: #2 ; Available includes #2 and #85
```

### After-state proof (this commit)

```bash
python3 scripts/issue_queue.py --repo matthewcorven/streaming-digest --limit 100 --format text
```

- `Next available: #2 [Task 0.2]` (was `#12`)
- `Available` includes `#2` and `#85` (both previously Blocked)
- No CLOSED issue appears in `Available`, `Next available`, or `Blocked`:
  `#1` (CLOSED, satisfies 0.1) and `#84` (CLOSED, satisfies 7.4) are used for resolution
  but never listed — verified in JSON (`state != "OPEN"` count is 0 in all three lists).
- `--state open` reproduces the legacy resolution view exactly (same available/blocked
  membership as the old default; only the new `(missing)` marker differs).
- `--mode status` board counts are identical to the pre-fix `--state all` baseline:
  Available 10, Blocked 82, Untriaged 0, Member-assigned 92, Open PR 0, Draft PR 0.
  Status mode still fetches OPEN issues once for board counts (no double-fetch when
  `--state open`; exactly one extra `gh` call when `--state all`).

### Phase-gate rewrites (17 issues)

Each head's `## Depends On` bare `X.0` ref was rewritten to the previous phase's last task
(in plan order). The new referent is a real OPEN issue, so the head stays blocked until its
phase genuinely starts — the under-reporting is fixed without mass-unblocking.

| Issue | Head | Old ref | New ref | New referent |
|---|---|---|---|---|
| #6 | [Task 1.1] | 1.0 | 0.5 | #5 |
| #54 | [Task 2.1] | 2.0 | 1.5 | #10 |
| #63 | [Task 3.1] | 3.0 | 2.6 | #62 |
| #65 | [Task 4.1] | 4.0 | 3.2 | #64 |
| #71 | [Task 5.1] | 5.0 | 4.6 | #70 |
| #76 | [Task 6.1] | 6.0 | 5.5 | #75 |
| #80 | [Task 7.1] | 7.0 | 4.6 | #70 |
| #86 | [Task 8.1] | 8.0 | 7.5 | #85 |
| #91 | [Task 9.1] | 9.0 | 8.5 | #90 |
| #11 | [Task 10.1] | 10.0 | 9.4 | #94 |
| #15 | [Task 11.1] | 11.0 | 5.5 | #75 |
| #24 | [Task 12.1] | 12.0 | 5.5 | #75 |
| #33 | [Task 13.1] | 13.0 | 8.4 | #89 |
| #36 | [Task 14.1] | 14.0 | 13.3 | #35 |
| #40 | [Task 15.1] | 15.0 | 14.4 | #39 |
| #45 | [Task 16.1] | 16.0 | 15.5 | #44 |
| #48 | [Task 17.1] | 17.0 | 16.3 | #47 |

After the rewrites, a full `--state all` scan reports **zero missing referents**.

> **Correction note (Run 2, 2026-07-25):** the table above is the **corrected** map, verified
> to match the live issue bodies exactly (17/17). The Run 1 / merged-ADR table was shifted
> down one row from row 4 (#65 onward) — it recorded each head's *next* head's referent
> (e.g. #65 → 4.6, #71 → 5.5) instead of its own. The live GitHub issue bodies were always
> correct (rewritten to the true previous-phase/slice last task); only the ADR/verification
> *table* was off-by-one. Run 2 below corrects the table and records the slice-order
> refinement and the 4 flagged exceptions.

### Environment

- **Host:** macOS arm64 (Apple Silicon) · Python 3.x · `gh` CLI authenticated
- **Repo state:** branch `matthewcorven-fix-issue-queue-readiness`, base `main` @ 123b8ae
- **Script:** `scripts/issue_queue.py` (368 lines before; +comment and `_format_reference` helper after)

### Test-convention decision

No automated test was added. Reason: the repo has **no test convention covering `scripts/`**.
`tests/` contains only .NET MSTest projects (`StreamingDigest.UnitTests`,
`StreamingDigest.IntegrationTests`); no pytest, no `conftest.py`, no Python test runner, and
no CI job under `.github/workflows/` executes Python tests. Per the task constraint, no new
framework or tooling was invented. The reproduction commands above are the recorded,
re-runnable evidence.

### Honesty boundary

- The `--mode status` "identical counts" claim compares against the pre-fix `--state all`
  baseline (the only apples-to-apples reference), since the pre-fix *default* status run was
  itself affected by the bug (it under-counted Available by 2).
- The 17 rewrites change GitHub issue bodies, not repo files; they are recorded here and in
  ADR-0017 but do not appear in the PR diff.
- The new Available set includes #101 itself (this issue) — expected, since it is OPEN,
  squad-labeled, and has no dependencies.

---

## Run 2 — 2026-07-25 (follow-up: ADR table correction + slice-order refinement)

### Scope

Two post-merge review findings on the #101 work, handled on a fresh branch off `origin/main`
(92a4c83). **The `scripts/issue_queue.py` fix is NOT reopened** — `--state all` default and the
`_format_reference` `(missing)` helper are unchanged and verified correct.

### Finding 1 — ADR/verification rewrite-map table was off-by-one (FIXED here, docs only)

The merged ADR-0017 and Run 1 verification table were shifted down one row starting at #65:
each wrong row listed the *next* head's referent, reading as a same-phase forward reference
(#65 Task 4.1 → 4.6, #71 Task 5.1 → 5.5, … #45 Task 16.1 → 16.3). The **live GitHub issue
bodies were always correct** — they were rewritten to the true previous-phase/slice last task.
Only the *recorded table* was wrong. Corrected above (17/17 now match live).

**Discrepancy flag for the reviewer:** a supplied "expected live values" table for this
follow-up asserted a *different* set of live values (e.g. #76=5.5→claim OK, #80=7.5, #15=11.7,
#24=12.8, #33=12.8, #36=14.4) that **does not match actual GitHub**. Verified directly against
`gh issue view` on 2026-07-25: live is `#65=3.2, #71=4.6, #76=5.5, #80=4.6, #86=7.5, #91=8.5,
#11=9.4, #15=5.5, #24=5.5, #33=8.4, #36=13.3, #40=14.4, #45=15.5, #48=16.3`. I used **live
GitHub as the source of truth** and did not alter the (correct) live bodies. Only 3 of the
supplied table's rows matched live (#65=3.2, #71=4.6, #86=7.5); the other 14 were the
*pre-correction* shifted values, suggesting the supplied table was generated from the stale
ADR, not from live. This is called out so the discrepancy is investigated rather than
silently propagated.

**Proof command** (ADR table vs live, prints 17× OK):

```bash
python3 - <<'PY'
import subprocess, re
heads=[6,54,63,65,71,76,80,86,91,11,15,24,33,36,40,45,48]
adr=open('docs/adr/0017-phase-gate-task-x0-references.md').read()
for n in heads:
    b=subprocess.run(['gh','issue','view',str(n),'--repo','matthewcorven/streaming-digest',
      '--json','body','--jq','.body'],capture_output=True,text=True).stdout
    dep=re.search(r'## Depends On\s*([\s\S]*?)## Blocked By',b)
    live=re.search(r'(\d+\.\d+[a-z]?)',dep.group(1)).group(1)
    m=re.search(r'\| #%d \[Task [^\]]+\] \| [\d.]+ \| ([\d.a-z]+) \| #(\d+) \|'%n,adr)
    print(n, live, m.group(1), 'OK' if m.group(1)==live else 'MISMATCH')
PY
```

### Finding 2 — slice-order refinement + 4 flagged edges (LEFT AS-IS, flagged for user ruling)

The merged ADR quoted the plan's *"phase numbering is a reference grouping, not the build
order"* and then encoded numeric phase order — a self-contradiction. The convention is now
corrected in ADR-0017 to: **a head's gate is completion of the previous *slice* in execution
order** (which coincides with the previous numeric phase for 13 of 17 heads, and differs for
the 4 below).

A review flagged 4 live edges as candidates that may invert slice build order. Re-pointing is
a **scheduling decision** (changes when work becomes available), not data cleanup, so per the
ruling's hard constraints these were **left as-is and flagged** rather than guessed. All 4 are
currently **Blocked** in the live queue (none Available), and every live referent is a real
OPEN issue — nothing is mass-unblocked, and no re-point could silently release work:

| Head | Slice | Live edge | Note |
|---|---|---|---|
| #15 [Task 11.1] | 5 | → 5.5 / #75 | Phase 11 is inside slice 5; a phase-number edge (11.6) would be a forward ref into the head's own slice. Live is slice-consistent. |
| #24 [Task 12.1] | 5 | → 5.5 / #75 | Phase 12 split across slices 5/12; 12.1 is the slice-5 "early 12" part. Live is slice-consistent. |
| #80 [Task 7.1] | 6 | → 4.6 / #70 | Phase 6 split across slices 5/9/10; a Phase-6 referent would be slice-inconsistent. Live is slice-consistent. |
| #33 [Task 13.1] | 11 | → 8.4 / #89 | Phase 13 is slice 11; current referent 8.4 (#89, slice 9). Live is slice-consistent. |

**Status:** open questions for the user. Any change requires an explicit user scheduling
ruling. **Ambiguity honestly noted:** "early 12" (slice 5's Phase-12 portion) is not enumerated
task-by-task in the plan, so the exact slice-5/slice-12 boundary within Phase 12 is not
machine-derivable from the plan text alone — reinforcing that these 4 are judgment calls, not
mechanical fixes.

### Queue state — BEFORE vs AFTER this follow-up

This follow-up changed **documentation only** (ADR-0017 + this evidence). No issue bodies were
modified, so the queue is unchanged by it. Both runs below are `python3 scripts/issue_queue.py
--repo matthewcorven/streaming-digest --limit 100 --format text`:

- **missing referents = 0** (confirmed `--state all`, before and after)
- Available = **9**: #2, #12, #29, #53, #57, #58, #59, #83, #85
- Next available = **#2** [Task 0.2]
- Blocked = **82** (squad task issues); Untriaged = 0; Member-assigned = 91; Open PR = 0; Draft PR = 0
- None of the 4 flagged heads (#15, #24, #80, #33) is Available — all remain Blocked.
  **(AFTER == BEFORE: no issue-body change was made.)**
