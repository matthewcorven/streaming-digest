### Task 12.5: Implement cluster ranking and similarity percentages

Requirements:

- API clusters document matches by video. Multiple result clusters must not reference the same video.
- Cluster title is video override title when present, otherwise original scraped title.
- Cluster score is a weighted aggregate score over submatches with note/user-signal boosts.
- Related items are drawn from across the whole corpus and expose `Relative similarity` percentages.
- Daily-digest high-signal matching uses a configurable global threshold, default 80%, against recent-search embeddings.

Verification:

- Unit tests cover weighted cluster-score calculation.
- Integration test confirms multiple segment matches from one video produce one cluster.
- High-signal query fixture returns expected items over the configured threshold.

