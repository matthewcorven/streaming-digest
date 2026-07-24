### Task 6.2: Implement audio-to-text provider abstraction

Source: `docs/architecture/ARCHITECTURE.md` §2.5, §5.5

Define application interface aligned with Semantic Kernel audio-to-text abstraction.

Methods:

- `TranscribeAsync(audioInput, options)`
- returns full text and timestamped cues.

Verification:

- Fake provider test.

