### Task 7.4: Prototype screenshot extraction approaches

Source: `docs/product/PRD.md` §2.2 (screenshot extraction); `.agents/skills/prototype/SKILL.md`

Requirements:

- Throwaway prototype comparing two candidate extraction approaches against the short test video fixture: (a) ffmpeg extracting frames from the downloaded video, reusing the Task 6.4 temp-media lifecycle; and (b) yt-dlp frame extraction.
- Evaluate from multiple angles: frame quality and timestamp accuracy, output file size and WebP encode cost, wall-clock speed, dependency/toolchain footprint on macOS ARM, Windows ARM, and Linux, fit with the temp-media lifecycle and quota, failure modes (missing stream, seek inaccuracy, partial download), and operational complexity in containers.
- Record the comparison and the chosen approach in an ADR (`docs/adr/`, next available number); the ADR closes the "screenshot extraction approach" open implementation decision.

Verification:

- Both approaches produce a WebP frame at a target timestamp from the same fixture.
- Comparison report committed per the Verification evidence convention.
- ADR records the decision with the evaluation matrix.

