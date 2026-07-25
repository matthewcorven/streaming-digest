#!/bin/bash
# THROWAWAY PROTOTYPE — WebP encode cost + size.
# This host's ffmpeg lacks libwebp, so WebP encode is measured via Python PIL
# (Pillow), which IS present. In the Debian/Ubuntu container, ffmpeg's libwebp
# does it natively (see report). Both numbers are recorded.
set -uo pipefail
cd "$(dirname "$0")"
python3 - <<'PY'
from PIL import Image
import time, os, glob
print("=== WebP encode via PIL (host ffmpeg has no libwebp) ===")
for src in sorted(glob.glob("results/ffmpeg/*-frame.png")):
    dst = src.replace("-frame.png", ".webp")
    im = Image.open(src)
    times = []
    for _ in range(5):
        t = time.perf_counter_ns(); im.save(dst, "WEBP", quality=80); times.append((time.perf_counter_ns()-t)//1_000_000)
    times.sort()
    print(f"  {os.path.basename(dst):34s} png={os.path.getsize(src):>7}B webp={os.path.getsize(dst):>6}B  encode_median={times[2]}ms spread={times[0]}-{times[-1]}ms")
PY
