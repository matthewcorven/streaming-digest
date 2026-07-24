### Task 12.3: Implement hybrid ranking

Formula:

- Document score: `document_score = textWeight * normalizedTextScore + vectorWeight * normalizedVectorScore`.
- Video cluster score: `base = 0.65 * max(document_score) + 0.25 * average(top 3 document_scores) + 0.10 * coverage_score`, where `coverage_score = min(distinctMatchedDocumentTypes / 4, 1.0)`.
- Add `note_boost = 0.08` when cluster has a matching note.
- Add `interaction_boost = min(0.05, 0.01 * recent_open_count_for_cluster)`.
- Final `cluster_score = min(1.0, base + note_boost + interaction_boost)`.
- UI label is `Relative similarity`; it is a normalized vector rank score within current result set, with tooltip explaining that it is relative to the query/model/result set and not confidence.

Return:

- score.
- score components.
- matched fields.
- explanation.
- snippets.

Verification:

- Unit/integration tests validate ordering with fixed data.
- Tooltip text explains `Relative similarity` semantics.

