#!/bin/bash
# THROWAWAY PROTOTYPE — Task 7.4, approach (a): ffmpeg direct frame extraction.
# Measures: wall-clock (median+spread over N runs), timestamp accuracy (PSNR vs
# reference frame extracted by frame number), output size, WebP encode cost.
# Prints results as they go. Deterministic: fixed fixtures, fixed targets.
set -uo pipefail
cd "$(dirname "$0")"
FIX=fixtures
OUT=results/ffmpeg
mkdir -p "$OUT"
RUNS=5

# target table: clip|seek_target|reference_frame_png|label
TARGETS=(
  "clipA-short.mp4|5.00|ref-A-5.00.png|A-keyframe-5.00"
  "clipA-short.mp4|5.37|ref-A-5.37.png|A-nonkeyframe-5.37"
  "clipB-long.mp4|65.00|ref-B-65.00.png|B-keyframe-65.00"
  "clipB-long.mp4|65.37|ref-B-65.37.png|B-nonkeyframe-65.37"
)

median() { printf '%s\n' "$@" | sort -n | awk '{a[NR]=$1} END{print (NR%2)? a[(NR+1)/2] : (a[NR/2]+a[NR/2+1])/2}'; }
min() { printf '%s\n' "$@" | sort -n | head -1; }
max() { printf '%s\n' "$@" | sort -n | tail -1; }

echo "=== ffmpeg direct extraction — $RUNS runs per target ==="
for row in "${TARGETS[@]}"; do
  IFS='|' read -r clip target ref label <<< "$row"
  echo
  echo "--- $label : clip=$clip target=${target}s ref=$ref ---"
  times=()
  for i in $(seq 1 $RUNS); do
    start=$(python3 -c 'import time;print(time.time_ns())')
    ffmpeg -y -loglevel error -ss "$target" -i "$FIX/$clip" -frames:v 1 "$OUT/$label-frame.png" 2>"$OUT/$label-err.txt"
    end=$(python3 -c 'import time;print(time.time_ns())')
    ms=$(( (end-start)/1000000 ))
    times+=($ms)
    echo "  run$i: ${ms}ms"
  done
  med=$(median "${times[@]}"); mn=$(min "${times[@]}"); mx=$(max "${times[@]}")
  echo "  wall-clock: median=${med}ms min=${mn}ms max=${mx}ms"

  # accuracy: PSNR of extracted frame vs reference (frame-number-selected)
  psnr=$(ffmpeg -loglevel info -i "$OUT/$label-frame.png" -i "$FIX/$ref" -filter_complex psnr -f null - 2>&1 | grep -o 'average:[0-9.inf]*' | cut -d: -f2)
  echo "  accuracy: PSNR vs reference = ${psnr} (inf = identical frame = perfect seek)"

  # WebP: host ffmpeg lacks libwebp, so encode is measured separately via PIL in
  # run-webp.sh. Record PNG size here; WebP numbers come from run-webp.sh.
  pngsz=$(stat -f%z "$OUT/$label-frame.png")
  echo "  png size=${pngsz}B (WebP encode measured in run-webp.sh — host ffmpeg has no libwebp)"
done

echo
echo "=== FAILURE MODES ==="
echo "--- audio-only input (no video stream) ---"
ffmpeg -y -loglevel error -ss 5.00 -i "$FIX/audio-only.m4a" -frames:v 1 "$OUT/fail-audio.png" 2>&1 | head -3
echo "  exit=$? (nonzero expected); produced file: $(ls "$OUT/fail-audio.png" 2>/dev/null || echo NONE)"
echo "--- truncated download (40% of clipA) ---"
ffmpeg -y -loglevel error -ss 5.00 -i "$FIX/clipD-truncated.mp4" -frames:v 1 "$OUT/fail-trunc.png" 2>&1 | head -3
echo "  exit=$?; produced file: $(ls "$OUT/fail-trunc.png" 2>/dev/null || echo NONE)"
