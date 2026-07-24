### Task 6.3: Implement local whisper service adapter

Source: `docs/product/PRD.md` §2.4

Preferred engine: whisper.cpp if compatible.

Requirements:

- CPU fallback.
- GPU configurable where available.
- Model configurable.
- Internal HTTP service matching `docs/api/API_SPEC.md` §21; a CLI engine (e.g. whisper.cpp) is wrapped inside the service as an implementation detail, not exposed as the contract.

Verification:

- Transcribe bundled tiny audio fixture.

