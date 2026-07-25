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
