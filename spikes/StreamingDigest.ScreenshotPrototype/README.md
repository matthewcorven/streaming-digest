# THROWAWAY PROTOTYPE — Task 7.4 Screenshot Extraction

> **This is throwaway spike code. It answers one question and must NOT be merged
> as an implementation.** Only the validated decision and the ADR graduate to main.
> Follows the precedent set by `spikes/StreamingDigest.VectorPrototype` (11.3a/11.3b)
> — lives in `spikes/`, deliberately NOT registered in `StreamingDigest.slnx`.

## The question

Which screenshot-extraction approach should Task 7.5 use: **(a) ffmpeg** extracting
frames from the downloaded video (reusing the Task 6.4 temp-media lifecycle), or
**(b) yt-dlp** frame extraction?

## The answer (spoiler)

**ffmpeg.** The investigation reframed the comparison: yt-dlp is a *downloader*, not
a frame extractor. It has no native arbitrary-timestamp frame extraction — its only
time-window feature (`--download-sections`) explicitly "Needs ffmpeg" and re-encodes.
So Path B turned out to be *yt-dlp downloads, then the SAME ffmpeg extracts*. The real
comparison is "extract from an already-local file" vs "download-then-extract". See
`docs/verification/7.4-screenshot-extraction.md` and the ADR.

## One command

```bash
bash spikes/StreamingDigest.ScreenshotPrototype/run-all.sh
```

Prerequisites on this host: `ffmpeg`, `python3` + Pillow, `yt-dlp` (pipx), `docker`
(for the Linux/container measurements, which are run separately and documented in the
report — they are not part of `run-all.sh`).

## What it does

1. `generate-fixtures.sh` — synthetic videos via `testsrc2` (burns a timestamp/frame
   counter into every frame). Ground truth is objective: frame N at rate R has
   PTS = N/R s, confirmed by ffprobe; reference frames extracted by exact frame number.
   - `clipA-short.mp4` (30s, 640x360, GOP=30), `clipB-long.mp4` (300s, 720p, GOP=90)
   - targets: keyframe-aligned (5.00s / 65.00s) and non-keyframe (5.37s / 65.37s)
   - failure fixtures: `audio-only.m4a`, `clipD-truncated.mp4` (40% of clip A)
2. `run-ffmpeg.sh` — Path A: direct `ffmpeg -ss T` extraction. Wall-clock median+spread
   over 5 runs, PSNR vs reference frame (inf = perfect seek), failure modes.
3. `run-webp.sh` — WebP encode cost + size via Python PIL (host ffmpeg lacks libwebp).
4. `run-ytdlp.sh` — Path B: `yt-dlp` downloads over a local HTTP server, then the same
   ffmpeg extraction. Also exercises `--download-sections` and failure modes.

All measurements print to stdout as they go. Raw frames/webp land in `results/`.

**`fixtures/` and `results/` are git-ignored** (deterministically regenerable;
committing ~150 MB of binary media would bloat the repo). Clone → run the one
command → both directories are recreated.

## Honesty boundary (local-HTTP substitution)

The yt-dlp path is exercised against a **local `python3 -m http.server`**, NOT real
YouTube (per the synthetic-data directive). That substitution does **not** exercise:
format selection (`-f`), DASH/HLS fragmented downloads, signature/`nsig` handling,
throttling, or partial-download resume against a real CDN. Those are real yt-dlp
behaviors that only make Path B *slower and more failure-prone* than measured here —
so the local numbers are a **best-case lower bound** for Path B's download cost.
