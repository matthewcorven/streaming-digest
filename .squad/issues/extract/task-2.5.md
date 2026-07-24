### Task 2.5: Implement model discovery/download onboarding

Requirements:

- Display a hard-coded installation-configuration list of supported Hugging Face/Ollama/Whisper model IDs and download commands. Do not attempt live hardware-based model viability detection in MVP.
- First-run can trigger selected model download through an internal service HTTP API that executes configured CLI commands against a mounted model volume.
- User may alternatively provide an existing host model path that is mounted into the container, or follow displayed CLI commands manually.
- Provide a refresh button to detect completion after file-path, mounted-model, or command-line setup.
- Confirm before embedding model changes after initial setup; on confirmation the Active Embedding Model pointer flips immediately, old-model embeddings become stale by derivation (ADR-0001), and the system enters Embedding Transition (ADR-0008) with a coverage-rebuilding banner until the bulk reprocess completes. Scheduled ingestion pauses during the transition and one catch-up scheduled run fires on completion, backfilling High-Signal evaluation for transition-era videos (ADR-0011).

Verification:

- Missing model shows options and command snippets.
- Inline download records verified model state.
- Embedding model switch asks confirmation, enters Embedding Transition, and vector search covers only new-model embeddings until the reprocess finishes.
- A scheduled run falling inside a transition window is skipped, and exactly one catch-up run fires when the transition completes.

