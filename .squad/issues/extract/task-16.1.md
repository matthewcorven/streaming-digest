### Task 16.1: Implement concurrency configuration defaults

Recommended MVP concurrency defaults (source: `docs/architecture/ARCHITECTURE.md` §9.1):

- Channels processed concurrently: `1`.
- Videos per channel concurrently: `1`.
- Screenshots concurrently: `1`.
- Embedding batch size: `16` short documents or adaptive token-budget batching.
- Website scrapes: global `2`, per-host `1`.
- Repository API calls: global `2`, per-host `1`.
- Whisper jobs: `1` globally.
- Local LLM classification/segmentation jobs: `1` globally.

These defaults prioritize reliability over throughput and should be configurable after the MVP works.

Verification:

- Each default is seeded as an app setting and respected by the relevant worker.

