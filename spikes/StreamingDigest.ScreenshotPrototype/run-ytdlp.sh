#!/bin/bash
# THROWAWAY PROTOTYPE — Task 7.4, approach (b): yt-dlp path.
# FINDING UP FRONT: yt-dlp has NO native arbitrary-timestamp frame extraction.
# Its only image outputs are pre-generated platform thumbnails (--write-thumbnail)
# and --download-sections, which "Needs ffmpeg". So the honest Path B is:
#   yt-dlp downloads the media over HTTP, then ffmpeg extracts the frame.
# That makes this a comparison of "download-then-extract" (B) vs "extract-directly
# from an already-local file" (A) — NOT a symmetric two-frame-extractor race.
set -uo pipefail
cd "$(dirname "$0")"
FIX=fixtures
OUT=results/ytdlp
DL="$OUT/downloads"
mkdir -p "$OUT" "$DL"
BASE=http://localhost:8899
RUNS=5

median() { printf '%s\n' "$@" | sort -n | awk '{a[NR]=$1} END{print (NR%2)? a[(NR+1)/2] : (a[NR/2]+a[NR/2+1])/2}'; }
min() { printf '%s\n' "$@" | sort -n | head -1; }
max() { printf '%s\n' "$@" | sort -n | tail -1; }

TARGETS=(
  "clipA-short.mp4|5.00|ref-A-5.00.png|A-keyframe-5.00"
  "clipA-short.mp4|5.37|ref-A-5.37.png|A-nonkeyframe-5.37"
  "clipB-long.mp4|65.00|ref-B-65.00.png|B-keyframe-65.00"
  "clipB-long.mp4|65.37|ref-B-65.37.png|B-nonkeyframe-65.37"
)

echo "=== yt-dlp download-then-ffmpeg-extract — $RUNS runs per target ==="
echo "(Path B = yt-dlp fetch over HTTP, then the SAME ffmpeg extraction as Path A)"
for row in "${TARGETS[@]}"; do
  IFS='|' read -r clip target ref label <<< "$row"
  echo
  echo "--- $label : url=$BASE/$clip target=${target}s ---"
  dl_times=(); ex_times=(); tot_times=()
  for i in $(seq 1 $RUNS); do
    rm -f "$DL/$clip"
    d0=$(python3 -c 'import time;print(time.time_ns())')
    yt-dlp -q --no-warnings -o "$DL/$clip" "$BASE/$clip" 2>"$OUT/$label-dlerr.txt"
    d1=$(python3 -c 'import time;print(time.time_ns())')
    ffmpeg -y -loglevel error -ss "$target" -i "$DL/$clip" -frames:v 1 "$OUT/$label-frame.png" 2>>"$OUT/$label-dlerr.txt"
    d2=$(python3 -c 'import time;print(time.time_ns())')
    dl=$(( (d1-d0)/1000000 )); ex=$(( (d2-d1)/1000000 )); tot=$(( (d2-d0)/1000000 ))
    dl_times+=($dl); ex_times+=($ex); tot_times+=($tot)
    echo "  run$i: download=${dl}ms extract=${ex}ms total=${tot}ms"
  done
  echo "  download: median=$(median "${dl_times[@]}")ms spread=$(min "${dl_times[@]}")-$(max "${dl_times[@]}")ms"
  echo "  extract : median=$(median "${ex_times[@]}")ms spread=$(min "${ex_times[@]}")-$(max "${ex_times[@]}")ms"
  echo "  TOTAL   : median=$(median "${tot_times[@]}")ms spread=$(min "${tot_times[@]}")-$(max "${tot_times[@]}")ms"
  psnr=$(ffmpeg -loglevel info -i "$OUT/$label-frame.png" -i "$FIX/$ref" -filter_complex psnr -f null - 2>&1 | grep -o 'average:[0-9.inf]*' | cut -d: -f2)
  echo "  accuracy: PSNR vs reference = ${psnr}"
done

echo
echo "=== yt-dlp --download-sections (its only time-window feature, uses ffmpeg) ==="
echo "--- clipA, section *5.00-5.10 (then extract first frame) ---"
yt-dlp -q --no-warnings --download-sections "*5.00-5.10" --force-keyframes-at-cuts \
  -o "$OUT/sectionA.mp4" "$BASE/clipA-short.mp4" 2>&1 | head -3
if [ -f "$OUT/sectionA.mp4" ]; then
  ffmpeg -y -loglevel error -i "$OUT/sectionA.mp4" -frames:v 1 "$OUT/sectionA-frame.png"
  echo "  produced section clip $(stat -f%z "$OUT/sectionA.mp4")B + first frame"
  ffprobe -v error -select_streams v:0 -show_entries frame=pts_time -of csv=p=0 "$OUT/sectionA.mp4" | head -1 | sed 's/^/  first frame pts: /'
fi

echo
echo "=== FAILURE MODES via yt-dlp ==="
echo "--- audio-only ---"
yt-dlp -q --no-warnings -o "$DL/audio-only.m4a" "$BASE/audio-only.m4a" 2>&1 | head -2
ffmpeg -y -loglevel error -ss 5.00 -i "$DL/audio-only.m4a" -frames:v 1 "$OUT/fail-audio.png" 2>&1 | head -2
echo "  extract produced file: $(ls "$OUT/fail-audio.png" 2>/dev/null || echo NONE)"
echo "--- truncated (40% of clipA) ---"
yt-dlp -q --no-warnings -o "$DL/clipD.mp4" "$BASE/clipD-truncated.mp4" 2>&1 | head -2
ffmpeg -y -loglevel error -ss 5.00 -i "$DL/clipD.mp4" -frames:v 1 "$OUT/fail-trunc.png" 2>&1 | head -2
echo "  extract produced file: $(ls "$OUT/fail-trunc.png" 2>/dev/null || echo NONE)"
