### Task 7.3: Implement LLM semantic refinement

Using Semantic Kernel + Ollama:

- Input deterministic chunks/cues.
- Output JSON segment boundaries/titles/summaries.
- Validate output against schema.
- Schema validation is the only MVP repair mechanism; invalid output is logged and written to stdout for development diagnostics, then deterministic chunks are used.

Verification:

- Unit test validates JSON parsing/fallback.
- Invalid LLM output logs diagnostic and keeps deterministic chunks.
- Integration test with local model optional.

