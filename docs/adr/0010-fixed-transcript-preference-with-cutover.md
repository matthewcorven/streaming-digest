# Fixed transcript preference order with cutover on upgrade

A video can accumulate multiple transcripts (`youtube_caption`, `youtube_auto_caption`, `local_whisper`), and a later Reprocess can discover a better one (e.g. author uploads human-edited captions weeks after Whisper ran). Nothing said which transcript drives search and segments, or what happens to cue-level artifacts when it changes.

We decided: exactly one active transcript per video, chosen by fixed automatic preference `youtube_caption` > `local_whisper` > `youtube_auto_caption`. Activating a higher-preference transcript is a transcript cutover: cue-level search documents are rebuilt, segments re-map to new cues by timestamp overlap within the same Segment Generation, and old cue overrides are preserved but inert. Manual transcript selection is MVP+.

## Consequences

- Whisper output outranks platform auto-captions — a deliberate call: a configured local model with full audio beats YouTube's compressed auto-ASR, and it preserves the value of the expensive Whisper fallback.
- Segment boundaries and screenshots are untouched by a transcript cutover; only cue text and cue-derived search documents change.
- The preference order is a product constant; exposing it as a setting is MVP+.
- `DATA_MODEL.md` §3.7 and IMPLEMENTATION_PLAN Task 6.1 should state the one-active rule and preference order.
