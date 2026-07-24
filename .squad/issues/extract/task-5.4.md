### Task 5.4: Implement video idempotency and degraded-channel handling

Source: `docs/architecture/ARCHITECTURE.md` §4.8; ADR-0003

Requirements:

- Normalize YouTube video URL by removing query string and use it as idempotency key, with YouTube video ID as canonical platform identifier.
- Normal daily ingestion skips already processed videos.
- Previously processed videos are reprocessed only through explicit user retry/reprocess actions.
- Adapter failures retry with exponential backoff for two retries, then circuit-break and mark the channel Degraded (ADR-0003): stored on the channel, entered after two consecutive adapter-stage failures.
- Degraded channels are skipped by scheduled ingestion, but each scheduled run performs a single lightweight probe (one metadata fetch) on Degraded non-Paused channels: success clears Degraded and the channel rejoins the run; failure increments the failure count.
- An active Deferment pauses the failure counter; Paused channels are never probed; channel-state precedence is Deleted > Paused > Degraded > Active.
- Failures without active retry may early-return for the affected item while allowing other items to continue.

Verification:

- Daily re-run does not duplicate or reprocess a processed video.
- Explicit retry processes selected failed stages/items.
- Two consecutive adapter failures mark the channel Degraded; a successful probe on the next scheduled run clears it.
- A Paused-Degraded channel is never probed and stays Degraded until unpaused.

