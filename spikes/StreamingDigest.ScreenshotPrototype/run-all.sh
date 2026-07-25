#!/bin/bash
# ============================================================================
# THROWAWAY PROTOTYPE — Task 7.4 one-command runner.
#
#   ONE COMMAND:  bash spikes/StreamingDigest.ScreenshotPrototype/run-all.sh
#
# Runs the full comparison end-to-end:
#   1. generate fixtures (synthetic, deterministic)
#   2. Path A: ffmpeg direct extraction (timing + accuracy + failure modes)
#   3. WebP encode cost/size (via PIL on this host)
#   4. Path B: yt-dlp download-then-ffmpeg-extract over a local HTTP server
# Prints all measurements to stdout as it goes. Deterministic and re-runnable.
#
# Prerequisites on this host: ffmpeg, python3+Pillow, yt-dlp (pipx), docker.
# ============================================================================
set -uo pipefail
cd "$(dirname "$0")"

PORT=8899

echo "################################################################"
echo "# Task 7.4 screenshot-extraction prototype — THROWAWAY"
echo "################################################################"

echo; echo ">>> [1/5] Generating synthetic fixtures"
bash generate-fixtures.sh

echo; echo ">>> [2/5] Path A — ffmpeg direct extraction"
bash run-ffmpeg.sh

echo; echo ">>> [3/5] WebP encode cost + size"
bash run-webp.sh

echo; echo ">>> [4/5] Starting local HTTP server on :$PORT for the yt-dlp path"
(cd fixtures && python3 -m http.server $PORT >/tmp/sd-spike-http.log 2>&1 &
 echo $! > /tmp/sd-spike-http.pid)
sleep 1.5
curl -sI "http://localhost:$PORT/clipA-short.mp4" | head -1

echo; echo ">>> [5/5] Path B — yt-dlp download-then-ffmpeg-extract"
bash run-ytdlp.sh

echo; echo ">>> Stopping local HTTP server"
HTTP_PID=$(cat /tmp/sd-spike-http.pid 2>/dev/null || true)
[ -n "${HTTP_PID}" ] && kill "${HTTP_PID}" 2>/dev/null || true

echo
echo "################################################################"
echo "# DONE. Raw outputs in results/. See docs/verification/7.4-*.md"
echo "################################################################"
