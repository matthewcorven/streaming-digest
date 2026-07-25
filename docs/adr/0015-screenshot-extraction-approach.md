# Screenshot extraction uses ffmpeg directly on the temp-media file, in the worker

The implementation plan framed the screenshot-extraction decision as "ffmpeg frame extraction vs yt-dlp frame extraction." A throwaway prototype (`spikes/StreamingDigest.ScreenshotPrototype/`, evidence in `docs/verification/7.4-screenshot-extraction.md` + `.json`) showed that framing is not a real two-horse race: yt-dlp is a downloader with no native arbitrary-timestamp frame extraction — every frame-level path in yt-dlp shells out to ffmpeg (`--download-sections` "Needs ffmpeg" and re-encodes; its only image output is platform thumbnails). The honest comparison is *extract from the already-downloaded local file* vs *download-then-extract*, and on every target the yt-dlp path's extract step produced byte-identical PSNR to plain ffmpeg, because it *is* plain ffmpeg plus a download step.

We decided: **the worker generates each segment's screenshot by running ffmpeg directly against the temp-media file already downloaded by ingestion (Task 6.4 lifecycle), encoding WebP via libwebp at the configured timestamp offset (default segment start + 5 s).** ffmpeg is baked into the worker container image; Python/yt-dlp are not — yt-dlp belongs to the download stage, not the screenshot stage.

## Considered options

- **yt-dlp as the extraction tool** — rejected: it has no frame-extraction capability of its own; it downloads, then delegates to ffmpeg. Using it for screenshots adds a Python runtime (~102 MB toolchain, +116 MB image) and a best-case 363–394 ms download (local HTTP lower bound — real YouTube adds format selection, DASH/HLS, nsig, throttling) for zero extraction gain, and operates outside the temp-media lifecycle.
- **Sidecar / dedicated Aspire resource for extraction** — rejected: extraction is a 44–80 ms stateless transform over a file the worker already holds; a separate container adds a network hop, a second image, and orchestration surface for a sub-100 ms operation.

## Evaluation matrix

| Axis | ffmpeg direct | yt-dlp path | Verdict |
|---|---|---|---|
| Frame quality | PSNR=inf (pixel-identical) on keyframe-aligned seeks | identical (same ffmpeg) | ffmpeg |
| Timestamp accuracy | 0-frame error keyframe; +1 frame (~30 ms) non-keyframe | identical | ffmpeg |
| Output size + WebP encode | WebP 4.3–5.4× smaller than PNG; encode 6–42 ms | identical | ffmpeg |
| Wall-clock speed | 44–80 ms/frame median | 409–470 ms (adds download) | ffmpeg |
| Dependency footprint | single binary, no runtime; macOS ARM, Linux arm64, Windows ARM (builds available — analyzed, not measured) | +Python runtime +pip supply chain on every platform | ffmpeg |
| Temp-media lifecycle/quota fit | operates on the Task 6.4 temp file, inside quota | re-downloads outside the lifecycle | ffmpeg |
| Failure modes | audio-only → exit 234 no file; truncated → exit 183 no file (clean, detectable) | identical | ffmpeg |
| Container complexity | +543 MB single apt layer (326→869 MB); Debian ffmpeg has libwebp natively | +659 MB total (985 MB), Python layer | ffmpeg |

## Consequences

- The worker image gains `ffmpeg` via one apt layer (production-needed, surfaced by this prototype; no AppHost.cs change was required for 7.4 — the screenshot volume wiring is a 7.5 implementation detail).
- **Homebrew ffmpeg (dev macOS) lacks libwebp and drawtext; the Debian/Ubuntu container build has both** — development-vs-production toolchain skew to remember: WebP encoding and any future burned-timestamp diagnostics work in the container, not necessarily on a Homebrew host.
- Non-keyframe timestamp targets land up to +1 frame (~30 ms at 30 fps) after the requested time — accepted; invisible for search-result thumbnails.
- Every measured failure (audio-only input, truncated download, seek failure) is a clean non-zero exit with **no partial file written** — so Task 7.5's "screenshots are never load-bearing" design (branded placeholder on absence + domain event + retryable row) triggers on simple file absence.
- Regenerating a video's screenshots after a segment/offset change costs ~44–80 ms per frame (~3–5 s for ≤60 segments) — cheap enough for an interactive user action.
- This closes the "screenshot extraction approach" open implementation decision from Task 7.4.
