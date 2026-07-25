#!/bin/bash
# THROWAWAY PROTOTYPE — Task 7.4 fixture generator.
# Generates synthetic test videos using testsrc2, which burns a running
# timestamp/frame counter into every frame (top-left). Ground truth is OBJECTIVE:
# frame N at rate R has PTS = N/R seconds, verified via ffprobe, and reference
# frames are extracted by exact frame NUMBER for PSNR comparison.
#
# NOTE: this host's Homebrew ffmpeg lacks libfreetype (no drawtext) and libwebp —
# both real toolchain-footprint findings recorded in the report. We therefore rely
# on testsrc2's built-in counter rather than drawtext overlays.
set -euo pipefail
cd "$(dirname "$0")/fixtures"

# --- Clip A: short, 30s, 640x360, 30fps, GOP=30 (keyframe every 1.0s) ---
# Targets: 5.00s (frame 150, keyframe-aligned), 5.37s (nearest frame 161 @5.3667, non-keyframe)
ffmpeg -y -loglevel error \
  -f lavfi -i "testsrc2=size=640x360:rate=30:duration=30" \
  -f lavfi -i "sine=frequency=440:duration=30" \
  -c:v libx264 -preset fast -crf 21 -g 30 -pix_fmt yuv420p \
  -c:a aac -shortest clipA-short.mp4

# --- Clip B: long, 300s, 1280x720, 30fps, GOP=90 (keyframe every 3.0s) ---
# Targets: 65.00s (frame 1950, keyframe-aligned), 65.37s (nearest frame 1961 @65.3667, non-keyframe)
ffmpeg -y -loglevel error \
  -f lavfi -i "testsrc2=size=1280x720:rate=30:duration=300" \
  -f lavfi -i "sine=frequency=440:duration=300" \
  -c:v libx264 -preset fast -crf 21 -g 90 -pix_fmt yuv420p \
  -c:a aac -shortest clipB-long.mp4

# --- Reference frames by exact frame NUMBER (objective ground truth) ---
ffmpeg -y -loglevel error -i clipA-short.mp4 -vf "select=eq(n\,150)"  -vframes 1 ref-A-5.00.png
ffmpeg -y -loglevel error -i clipA-short.mp4 -vf "select=eq(n\,161)"  -vframes 1 ref-A-5.37.png
ffmpeg -y -loglevel error -i clipB-long.mp4 -vf "select=eq(n\,1950)" -vframes 1 ref-B-65.00.png
ffmpeg -y -loglevel error -i clipB-long.mp4 -vf "select=eq(n\,1961)" -vframes 1 ref-B-65.37.png

# --- Clip C: audio-only (no video stream) — failure-mode fixture ---
ffmpeg -y -loglevel error -f lavfi -i "sine=frequency=440:duration=30" -c:a aac audio-only.m4a

# --- Clip D: truncated download — first 40% of clip A, cut mid-stream ---
FULL_SIZE=$(stat -f%z clipA-short.mp4)
head -c $((FULL_SIZE * 40 / 100)) clipA-short.mp4 > clipD-truncated.mp4

echo "Fixtures + reference frames generated:"
ls -la *.mp4 *.m4a *.png
