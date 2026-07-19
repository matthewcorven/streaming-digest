# Fixed transcript preference order with cutover on upgrade

A video can accumulate multiple transcripts (`youtube_caption`, `youtube_auto_caption`, `local_whisper`), and a later Reprocess can discover a better one (e.g. author uploads human-edited captions weeks after Whisper ran). Nothing said which transcript drives search and segments, or what happens to cue-level artifacts when it changes.

We decided: exactly one active transcript per video, chosen by fixed automatic preference `youtube_caption` > `local_whisper` > `youtube_auto_caption`. Activating a higher-preference transcript is a transcript cutover: cue-level search documents are rebuilt, segments re-map to new cues by timestamp overlap within the same Segment Generation, and old cue overrides are preserved but inert. Manual transcript selection is MVP+.

## Consequences

- Whisper output outranks platform auto-captions — a deliberate call: a configured local model with full audio beats YouTube's compressed auto-ASR, and it preserves the value of the expensive Whisper fallback.
- Segment boundaries and screenshots are untouched by a transcript cutover; only cue text and cue-derived search documents change.
- The preference order is a product constant; exposing it as a setting is MVP+.
- `DATA_MODEL.md` §3.7 and IMPLEMENTATION_PLAN Task 6.1 should state the one-active rule and preference order.

## Amendment: inert cue overrides get a domain event, not carry-forward

We considered carrying cue overrides forward onto the new transcript by fuzzy text/timestamp matching (the way Orphaned Notes are surfaced after re-segmentation) and rejected it: a mis-applied override silently corrupting the authoritative transcript is worse than a lost one, and the fixed-preference rule means the new transcript is clean by definition. Overrides on the old transcript stay inert — no carry-forward, no approval gate.

To keep that honesty visible, a cutover records a domain event noting how many cue overrides became inert ("Transcript upgraded to author captions; 12 cue edits on the previous transcript are now inert"), so the run detail and video event surfaces show what happened instead of silently discarding curation work.
