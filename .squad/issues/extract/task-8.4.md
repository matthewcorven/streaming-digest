### Task 8.4: Local LLM classification

Source: `docs/product/PRD.md` §2.4; ADR-0007 (only active corrections feed few-shot examples)

Use:

- local model via Semantic Kernel/Ollama.
- JSON schema output.
- corrections as few-shot examples.

Verification:

- Invalid LLM output falls back safely.
- Correction history influences prompt construction.

