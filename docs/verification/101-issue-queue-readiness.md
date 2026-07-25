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
| #65 | [Task 4.1] | 4.0 | 4.6 | #70 |
| #71 | [Task 5.1] | 5.0 | 5.5 | #75 |
| #76 | [Task 6.1] | 6.0 | 6.4 | #79 |
| #80 | [Task 7.1] | 7.0 | 7.5 | #85 |
| #86 | [Task 8.1] | 8.0 | 8.5 | #90 |
| #91 | [Task 9.1] | 9.0 | 9.4 | #94 |
| #11 | [Task 10.1] | 10.0 | 10.3 | #14 |
| #15 | [Task 11.1] | 11.0 | 11.7 | #23 |
| #24 | [Task 12.1] | 12.0 | 12.8 | #32 |
| #33 | [Task 13.1] | 13.0 | 13.3 | #35 |
| #36 | [Task 14.1] | 14.0 | 14.4 | #39 |
| #40 | [Task 15.1] | 15.0 | 15.5 | #44 |
| #45 | [Task 16.1] | 16.0 | 16.3 | #47 |
| #48 | [Task 17.1] | 17.0 | 16.3 | #47 |

After the rewrites, a full `--state all` scan reports **zero missing referents**.

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

## Run 2 — 2026-07-25 (post-review corrections: ADR table off-by-one + 4 slice-order re-points)

Two defects found on coordinator review of PR #102, both ruled on by the user. **The script fix
(`--state all` default, `(missing)` helper) is unchanged and re-verified correct.**

### Defect A — ADR-0017 rewrite-map table was off-by-one (13 of 17 rows)

The **live issue bodies were always correct**; only the table *inside* the ADR was wrong —
shifted down one row starting at row 4, so each incorrect row listed the *next* head's referent
(reading as a same-phase circular dependency). Fixed the table to match live. This mattered
because implementers read the ADR, not the findings — a re-applied table would have created 13
real deadlocks.

### Defect B — 4 of the 17 edges inverted the plan's build order (numeric-phase vs slice order)

The original rule ("bare `X.0` → previous *phase's* last task") hard-coded numeric phase order,
contradicting the plan's own line 1743: *"Execution order is the vertical slices below; phase
numbering is a reference grouping for requirements, not the build order."* The slice list
interleaves phases. 4 edges ran backwards and were re-pointed to the last task of the preceding
*slice*:

| Head | Head slice | Old referent (slice) | New referent (slice) | Rationale |
|---|---|---|---|---|
| #15 [Task 11.1] | 5 | 10.3 #14 (slice 8) | **5.5 #75** (slice 4) | M1 (slices 1-5) was blocked behind website scraping (slice 8) |
| #24 [Task 12.1] | 5 | 11.7 #23 (slice 12) | **5.5 #75** (slice 4) | M1 was blocked behind 7 later slices (slice 12) |
| #80 [Task 7.1] | 6 | 6.4 #79 (slice 10) | **4.6 #70** (slice 3) | segmentation was blocked behind Whisper fallback (slice 10) |
| #33 [Task 13.1] | 11 | 12.8 #32 (slice 12) | **8.4 #89** (slice 8) | inverted by one slice |

Slice derivation (positional numbering; the plan has a duplicate "5" typo): slice 4 = "Basic
yt-dlp metadata ingestion (Phase 5)", last task 5.5 (#75). Slice 3 = "Phases 2-4", last task 4.6
(#70). Slice 8 = "Local LLM classification/semantic segmentation (8.4, 7.3)", last task 8.4
(#89). For #24, "early 12" in slice 5 is taken to mean the search-readiness subset (12.1-12.4);
its only outstanding predecessor outside the slice is slice 4 → 5.5 (#75). The other 13 edges are
unchanged because numeric phase order and slice order coincide there.

### Hard constraint held — no mass-unblock

Each of the 4 re-pointed heads is **still blocked** behind a real OPEN issue: #15→#75, #24→#75,
#80→#70, #33→#89. **None silently became Available.** Verified:

```
python3 scripts/issue_queue.py --repo matthewcorven/streaming-digest --limit 100 --format text
# - #15 [Task 11.1] ... (depends on #75)   <- Blocked list, not Available
# - #24 [Task 12.1] ... (depends on #75)
# - #33 [Task 13.1] ... (depends on #89)
# - #80 [Task 7.1]  ... (depends on #70)
```

### Counts before vs after the re-point

| Metric | Before re-point | After re-point | Explanation |
|---|---|---|---|
| Available | 10 | 9 | **#101 closed** (this issue) after PR #102 opened — not a re-point effect |
| Blocked | 82 | 82 | unchanged |
| Member-assigned | 92 | 91 | **#101 closed** — one fewer open assigned issue |
| Missing referents | 0 | 0 | unchanged |

The re-point itself changed **zero** availability and **zero** blocked count. The only delta is
#101 transitioning OPEN→CLOSED, which removes it from both Available and Member-assigned. This is
expected and unrelated to the dependency re-points.

### Re-run command

```
python3 scripts/issue_queue.py --repo matthewcorven/streaming-digest --limit 100 --format text
python3 scripts/issue_queue.py --repo matthewcorven/streaming-digest --limit 100 --format text --mode status
```

### Label re-audit (auto-triage caution)

Re-audited labels on all 4 touched issues after editing: #15 `squad,squad:neo,phase-11,slice-5`;
#24 `squad,squad:neo,phase-12,slice-5`; #80 `squad,squad:tank,phase-7,slice-6`;
#33 `squad,squad:tank,phase-13,slice-11`. No auto-triage mislabeling introduced.
