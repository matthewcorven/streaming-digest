# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest relies on Aspire orchestration and Docker Compose creation for local and deployment workflows.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-07-24 (via Morpheus, user-approved plan resolutions — depth/evidence pass): New work in your lane — **Task 7.4** is a screenshot toolchain prototype: ffmpeg frame extraction vs yt-dlp frame extraction against the test fixture, evaluated on quality/accuracy, size/encode cost, speed, toolchain footprint (macOS ARM, Windows ARM, Linux), temp-media fit, failure modes, and container complexity. The outcome is recorded in an ADR (next available number); the actual screenshot-generation task is renumbered to **7.5** and follows the ADR. **Task 6.4** is retitled "Implement temporary media lifecycle and transcription fallback" and now owns the shared temp-media lifecycle (quota, filename scheme, cleanup) for ALL pipeline stages — transcription, screenshot frame extraction (7.5), and anything future — design it stage-agnostic. **Task 10.1a** now includes a typed scraper client matching the internal scraper API contract (`docs/api/API_SPEC.md` §20), including health-check integration.
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

📌 2026-07-24 (via Coordinator, user directive): New **Task 11.3a "Prototype vector knowledge-base approach"** (slice 4, after 11.3) informs your lane — its pgvector index trade-off evaluation provides the evidence for the open **HNSW vs IVFFlat** index decision (previously unresolved in the plan's open implementation decisions; now marked "informed by Task 11.3a"). Prototype policy: synthetic programmatically generated data only — no AI, no token/latency cost, controlled content profile.

## Learnings

Dozer owns orchestration, environment wiring, and container topology.
📌 2026-07-24 (via Coordinator, user directive — prototypes-first sequencing): **Task 7.4** (screenshot toolchain prototype: ffmpeg vs yt-dlp) now runs in slice 2 "Prototypes first", immediately after slice 1 foundation — alongside 11.3a/11.3b. Its HNSW vs IVFFlat evidence (via 11.3a, also slice 2) arrives before embedding infrastructure is built; the ADR and renumbered Task 7.5 follow as before.

## 2026-07-25 — Task 7.4: Screenshot extraction prototype (issue #84)

📌 **Outcome: ffmpeg direct extraction, recorded in ADR-0015.** Built `spikes/StreamingDigest.ScreenshotPrototype/` (throwaway, not in solution). One command: `bash spikes/StreamingDigest.ScreenshotPrototype/run-all.sh`. Evidence: `docs/verification/7.4-screenshot-extraction.md` + `.json`.

**Key learnings:**
- The plan's "ffmpeg vs yt-dlp" framing was NOT a real two-horse race — yt-dlp is a downloader with NO native frame extraction; every frame-level path delegates to ffmpeg (`--download-sections` "Needs ffmpeg", re-encodes; only image output is platform thumbnails). Proved by byte-identical PSNR between Path A (ffmpeg direct) and Path B (yt-dlp download → same ffmpeg extract). Reframing a wrong comparison is a valid, valuable prototype outcome.
- ffmpeg extract: 44–80 ms/frame median; keyframe-aligned seeks pixel-perfect (PSNR=inf), non-keyframe lands +1 frame (~30 ms) — `-ss` decodes to next frame at/after target.
- **Homebrew ffmpeg 8.1.2 (macOS) lacks libwebp AND libfreetype/drawtext; the Debian/Ubuntu container build (6.1.1) has BOTH.** Dev-vs-prod toolchain skew — WebP + burned-timestamp diagnostics work in container, not necessarily on Homebrew host. Pillow (pip) is the host WebP fallback.
- WebP q80 = 4.3–5.4× smaller than PNG; encode 6–42 ms.
- Container images (linux/arm64): base .NET runtime 326 MB → +ffmpeg 869 MB (+543 MB) → +python+yt-dlp 985 MB (+116 MB more). Worker image gets ffmpeg only — yt-dlp belongs to download stage, not screenshots.
- yt-dlp install on macOS: pip blocked by PEP 668 → use pipx; dragged in ~102 MB (14 MB venv + 88 MB Python runtime). ffmpeg needs no runtime.
- Failure modes all clean & no-partial-file: audio-only → exit 234, truncated → exit 183 ("moov atom not found"). Feeds Task 7.5's placeholder-on-absence design directly.
- Windows ARM: ANALYZED not measured (can't execute) — BtbN publishes winarm64 ffmpeg builds; yt-dlp_arm64.exe bundles Python. Never present analysis as measurement.
- Synthetic fixtures via `testsrc2` (built-in timestamp/frame counter) give OBJECTIVE ground truth: frame N at rate R = N/R s, verified by ffprobe; reference frames by exact frame number → PSNR. Better evidence than eyeballing real video.
- No AppHost change needed for 7.4 (unlike 11.3a's production pgvector pin). Extraction runs in-process in worker; screenshot volume wiring is a 7.5 detail.

## 2026-07-25 — #101: issue_queue.py readiness fix + Task X.0 phase-gate ruling

📌 **Outcome: readiness bug fixed (default `--state open`→`all`); phase-gate refs ruled and rewritten; ADR-0017 written.** Evidence: `docs/verification/101-issue-queue-readiness.md` + `.json`. Decision: `docs/adr/0017-phase-gate-task-x0-references.md`. Issue #101, labels audited to exactly `squad` + `squad:dozer`.

**Key learnings:**
- The helper's `--state open` default made `fetch_issues` retrieve only OPEN issues, so a CLOSED dependency referent resolved to None and was scored UNSATISFIED — a completed prerequisite read as a blocker. The defect compounds: the more work closes, the more false blockers. Fix was the one-line default flip, NOT editing six doc files — flipping the default makes every already-documented invocation correct at once. The report filters (`state == "OPEN"`) already excluded closed issues from all output lists, so `all` is safe for resolution.
- Verify the fix against the RIGHT baseline: pre-fix default `--mode status` was itself buggy (under-counted Available by 2). Compared after-state counts to the pre-fix `--state all` baseline (Available 10 / Blocked 82 / Untriaged 0 / Assigned 92 / PR 0/0) — identical.
- `--state open` kept as explicit opt-in; in `--mode status` + `--state open` the open fetch is reused (no wasteful double-fetch); under the new `all` default status mode makes exactly one extra `gh` call for board counts.
- 17 phase-chain heads depended on bare `X.0` refs with NO existing referent (no `[Task X.0]` issue in any state, zero `### Task X.0:` in the retired plan). These were migration artifacts — in the plan a phase had no body, so its first task depended on the phase itself. Plan's own sequencing says "phase numbering is a reference grouping, not the build order."
- Ruled option (d) rewrite-to-real-issue (previous phase's last task in plan order) + (c) `(missing)` marker as safety net. Did NOT mass-unblock: each of the 17 still depends on a real OPEN issue and stays blocked until it closes. After rewrite, zero missing referents remain.
- Added a `_format_reference` helper so a missing referent renders as `ref (missing)` in both queue and status text — a broken ref can never again masquerade as a routine open dependency. JSON already carried `missing: true`; the gap was text-only.
- No automated test added: NO test convention covers `scripts/` (tests/ is .NET MSTest only; no pytest/conftest/CI Python job). Recorded exact reproduction commands in the verification doc instead of inventing tooling.
- Auto-triage workflow strikes again: stamped wrong `squad:trinity` + `go:needs-research` on #101. Removed; verified exactly one owner label remains. Always audit labels after issue creation.

## 2026-07-25 — ADR-0017 follow-up (post-#102 review findings), branch `matthewcorven-fix-adr-0017-slice-order`

- Coordinator reported two defects after #102 merged. Script fix (`--state all` default, `_format_reference`) verified correct and NOT reopened.
- **Finding 1 (FIXED, docs-only):** merged ADR-0017 rewrite-map table was off-by-one — shifted down one row from #65 onward (each wrong row listed the NEXT head's referent = apparent same-phase forward ref). LIVE GitHub bodies were always correct; only the recorded table was wrong. Corrected ADR table now matches live 17/17 (verified programmatically against `gh issue view`).
- **INTEGRITY FLAG:** the supplied 'expected live' table for this task asserted values matching the STALE shifted ADR (e.g. #80=7.5, #15=11.7, #24=12.8, #33=12.8, #36=14.4), NOT actual GitHub. Verified live is #65=3.2,#71=4.6,#76=5.5,#80=4.6,#86=7.5,#91=8.5,#11=9.4,#15=5.5,#24=5.5,#33=8.4,#36=13.3,#40=14.4,#45=15.5,#48=16.3. Used LIVE as source of truth; did NOT touch the correct live bodies. Only 3 of 17 supplied rows matched live → supplied table was generated from stale ADR, not live. Flagged in the verification doc + PR body.
- **Finding 2 (LEFT AS-IS, flagged):** merged ADR quoted the plan's "phase numbering is not the build order" then encoded numeric phase order — self-contradiction. Corrected convention in ADR-0017 to previous-SLICE completion (coincides with previous numeric phase for 13/17 heads). 4 edges (#15→5.5/#75, #24→5.5/#75, #80→4.6/#70, #33→8.4/#89) flagged as candidate slice-order inversions but NOT re-pointed — re-pointing is a SCHEDULING decision, so per the ruling's own hard constraints they were left as-is and flagged for user ruling. All 4 currently Blocked (none Available); live referents are real OPEN issues. 'early 12' boundary not enumerated in plan → these are judgment calls.
- ADR-0017 edited in place (not a new 0018 — same convention). Evidence: docs/verification/101-issue-queue-readiness.{md,json} Run 2. Queue unchanged (docs-only): missing=0, Available=9, Next=#2, Blocked=82.
- Lesson: ALWAYS verify a supplied 'expected values' table against live before acting — do not assume a delegated diff-table is accurate; it may have been generated from the same stale artifact being corrected.

## 2026-07-25 — ADR-0017 final ruling applied on PR #103

- User ruled the governing convention is **real upstream data/capability dependency**, not previous-slice adjacency. Rewrote ADR-0017 accordingly and removed the layered "previous phase" / "previous slice" / "exception notes" framing. Final rule: rewrite a bare `Task X.0` ref to the issue that produces what the head actually consumes; if no upstream issue exists, the head has no dependency.
- Updated live issue bodies to match the ruling: **#80 `4.6/#70 -> 5.5/#75`** (author chapters come from yt-dlp metadata ingestion), **#24 `5.5/#75 -> 11.2/#16`** (search SQL consumes `search_documents`, so the real prerequisite is the search-document generator), **#33 `8.4/#89 -> 11.1/#15`** (override APIs are built on the original/override/effective contract + `field_override_history`, so the gate is the effective-value service), and **#36 `13.3/#35 -> none`** (Matrix SDK selection is a research/evaluation task with no upstream issue dependency). **#15 stays `5.5/#75`** — confirmed as-is.
- Re-generated the ADR table FROM LIVE GITHUB after the body edits and verified **17/17 match**, including `#36 = None`. Updated `docs/verification/101-issue-queue-readiness.{md,json}` Run 3 to keep live bodies, ADR, and evidence in sync.
- Measured queue impact explicitly: missing referents stayed **0**; Available **9 -> 10** and Blocked **82 -> 81** because **only `#36`** moved from Blocked to Available when its invented dependency was cleared. `#15`, `#24`, `#33`, and `#80` all remained Blocked behind real OPEN issues. No silent mass-unblock.
- Recount under the final rule: **12 of 17** heads still coincide with the previous numeric phase's last task; the five divergences are `#15`, `#24`, `#33`, `#36`, and `#80`.
- New finding flagged, not silently expanded into this correction: if the Matrix notification path needs an explicit stored-Digest dependency, it belongs on implementation issues `#37` / `#38`, not on SDK-selection issue `#36`.

## 2026-07-25 — Cross-agent update (via Scribe): prototype series outcomes + Morpheus rulings affecting you

- Your Task 7.4 prototype (ADR-0015, PR #98 — merged) was fully CONFIRMED in Morpheus's synthesis review. Rulings touching your lane: **+543 MB ffmpeg in the worker is acceptable**; **+1-frame (~30 ms) non-keyframe offset does not matter** because screenshots are never load-bearing; coordinator owes an ARCHITECTURE §12 note on Homebrew-vs-Debian ffmpeg build skew; spike code stays on main as evidence under the `spikes/README.md` convention.
- Series context: Neo's 11.3a (PR #96) proved the vector knowledge-base stack and pinned the pgvector AppHost image as production-needed; Neo's 11.3b (PR #97) proved the DATA_MODEL §6 search mechanics and surfaced the 447 ms hybrid-latency finding that Task 12.3 must design around. Synthesis evidence: `docs/verification/prototype-synthesis-11.3a-11.3b-7.4.md` (PR #99).
## 2026-07-25 — Cross-agent update (via Scribe): prototype series outcomes + Morpheus rulings affecting you

Your Task 7.4 prototype (ADR-0015, PR #98 — merged) was fully CONFIRMED in Morpheus's synthesis review (18 CONFIRM / 1 OVERTURN overall; the overturn was Neo's 11.3a "no ADR" call, not your work). Rulings touching your lane: (1) **+543 MB ffmpeg in the worker ACCEPTABLE** — ARCHITECTURE §2.2/§9.1 presuppose it; in-process validated over sidecar. (2) **+1-frame (~30 ms) non-keyframe offset DOES NOT MATTER** — screenshots never load-bearing; absence → placeholder; no design change. (3) Coordinator owes an ARCHITECTURE §12 note on Homebrew-vs-Debian ffmpeg build skew (dev-machine vs container ffmpeg differences). (4) Spike code stays on main as evidence under the new `spikes/README.md` convention (throwaway, excluded from slnx, evidence-linked, never imported by production).

**Series context you didn't see:** Neo's 11.3a (PR #96) proved the vector knowledge-base stack and pinned the pgvector/pgvector:pg17 image in the AppHost — production-needed wiring now on main; note the named-volume gotcha (`docker volume rm streamingdigest-postgres-data` if the volume was initialized by stock postgres). Neo's 11.3b (PR #97) proved §6 search mechanics and surfaced a 447 ms hybrid-latency finding that Task 12.3 must design around. Synthesis doc: `docs/verification/prototype-synthesis-11.3a-11.3b-7.4.md` (PR #99, open).

## 2026-07-28 — PR #181 revision re-reviewed (commit 19b67a8ab321c2f259390a3b70cfc7d40d9a66af)

Morpheus completed independent re-review of PR #181 revision after commit 19b67a8ab321c2f259390a3b70cfc7d40d9a66af. **Verdict: ready for review.** Task 12.x threshold calibration against the real embedding provider confirmed; fresh installs seed 70; upgrade paths preserve existing stored values unless changed deliberately. PR #181 now advancing to maintainer review.
