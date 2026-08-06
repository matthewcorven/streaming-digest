# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest relies on Aspire orchestration and Docker Compose creation for local and deployment workflows.

## Standing Learnings

- Dozer owns orchestration, environment wiring, and container topology.
- Team decisions are classified before inbox write; architectural calls become ADR pointers in `decisions.md`.
- Prototype-first sequencing matters in this lane: Task 7.4 precedes 7.5; Task 6.4 owns shared temp-media lifecycle; Task 10.1a includes the typed scraper client.

## Key History

### 2026-07-25 — Task 7.4 screenshot extraction prototype (issue #84)

- Outcome recorded in ADR-0015: use ffmpeg directly on the temp-media file in the worker.
- Prototype disproved the original ffmpeg-vs-yt-dlp framing: yt-dlp is not a native frame extractor and delegates frame work to ffmpeg.
- Key evidence: ffmpeg extraction was ~44–80 ms/frame; Homebrew ffmpeg lacked some codecs/features present in the Debian container build; baking ffmpeg into the worker added ~543 MB but avoided the extra Python/yt-dlp runtime.
- Failure modes stayed clean (`audio-only` and truncated media) and directly informed Task 7.5 placeholder behavior.

### 2026-07-25 — Issue-queue readiness and ADR-0017 ruling (issue #101)

- Fixed `scripts/issue_queue.py` so CLOSED dependency referents resolve correctly by changing the default fetch state to `all`.
- Added `(missing)` output so unresolved dependency references cannot masquerade as ordinary open blockers.
- Final ADR-0017 rule: bare `Task X.0` references mean the real upstream data/capability dependency; if no upstream issue exists, there is no dependency.
- Verified the rewrite without silently mass-unblocking the queue.

### 2026-07-28 — PR #181 revision re-review

- Morpheus re-reviewed the revision commit `19b67a8ab321c2f259390a3b70cfc7d40d9a66af` and cleared PR #181 for maintainer review.
- Confirmed threshold calibration against the real embedding provider, default seed 70 for fresh installs, and upgrade safety for existing stored values.

### 2026-08-02 — Ralph cycle status (issue #210 / PR #229)

- Branch `squad/210-whisper-runtime` completed one revision cycle and reached maintainer-ready state.
- Morpheus first-pass feedback landed in commit `2db731e`; independent adversarial re-review approved the final artifact at 100%.
- Coordinator later confirmed the original `z-ai/glm-5.2` runtime came from top-level `create_session.model` placement; `kickoff.model` is the required shape.
// 2026-08-03T12:41:41Z Dozer: PR #228 DI lifetime fix pushed (fdc14d9). IModelRuntimeClient singleton->transient; OllamaModelRuntimeClient takes IHttpClientFactory, resolves 'ollama-runtime' per op. 436/436 unit tests green, CI test pass (3m29s). Awaiting Morpheus re-review of G2.
