## Open implementation decisions

These are implementation-time choices, not product-scope blockers:

- Exact Matrix SDK/service technology.
- Exact whisper engine behind the HTTP audio-to-text service contract (whisper.cpp preferred); the service shape itself is decided (`docs/api/API_SPEC.md` §21).
- Exact Ollama LLM model default per hardware.
- HNSW vs IVFFlat pgvector index based on installed pgvector version and expected dataset size (informed by Task 11.3a prototype evidence).
- Whether Crawlee/Playwright runs in worker container or separate scraper container.
- Screenshot extraction approach (ffmpeg vs yt-dlp frame extraction) — resolved by the Task 7.4 prototype and recorded in an ADR.
- Ranking weight defaults — informed by Task 11.3b prototype evidence.

