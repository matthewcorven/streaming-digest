### Task 6.4: Implement temporary media lifecycle and transcription fallback

Source: `docs/architecture/ARCHITECTURE.md` §4.2; `docs/architecture/DATA_MODEL.md` §3.2 (`ingestion.tempMedia.maxBytes`)

Owns the shared temp-media lifecycle for every pipeline stage that downloads temporary media — transcription audio/video (below), screenshot frame extraction (Task 7.5), and any future stage.

If no usable YouTube transcript:

1. Download temporary audio/video into the service child `temp/` folder.
2. Enforce configurable temp-media quota, defaulted during first run to 50% of then-available free disk bytes.
3. Transcribe locally.
4. Delete temporary media after success and failure.
5. Run startup cleanup after a brief delay and post-daily-run cleanup.
6. Store transcript and cues.

Filename scheme:

- `{runId}/{videoId}/{stage}-{attempt}-{contentHashPrefix}.{ext}`

Verification:

- Test confirms temp file deletion after success and failure.
- Startup cleanup removes orphan temp files.
- Lost temp media causes the stage to repeat rather than corrupting state.
- The same quota, filename scheme, and cleanup jobs apply to screenshot frame-extraction media and any other temporary pipeline media.

## Phase 7: Segmentation and screenshots

