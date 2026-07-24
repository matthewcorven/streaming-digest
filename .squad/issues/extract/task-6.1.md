### Task 6.1: Implement caption/transcript ingestion

Store:

- `video_transcripts`
- `transcript_cues`

Exactly one active transcript per video, chosen by fixed preference `youtube_caption` > `local_whisper` > `youtube_auto_caption` (ADR-0010). A Reprocess that discovers a higher-preference transcript performs a transcript cutover: cue-level search documents are rebuilt from the new cues, segments re-map by timestamp overlap within the same Segment Generation, and cue overrides on the old transcript are preserved but inert. Manual transcript selection is MVP+.

Verification:

- Fixture transcript stored with timestamps.
- Cutover integration test: auto-caption transcript active, then author captions arrive on Reprocess and become active with documents rebuilt.
- Cutover records a domain event counting cue overrides that became inert (ADR-0010 amendment); no carry-forward to the new cues.

