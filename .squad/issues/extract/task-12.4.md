### Task 12.4: Implement search UI

Source: `docs/product/PRD.md` §2.5; `docs/api/API_SPEC.md` §8

Features:

- Natural-language query box for MVP. Advanced query syntax is MVP+.
- Filters for channel, date range, result type, has transcript, has repo, has notes, and ingestion status. Link-classification hide/show filtering is MVP+.
- Global app setting for text/vector ranking weights.
- Video-clustered ranked list.
- Collapsed result card shows title, channel, publish date, note indicator/button, processing/stale/failed indicator, retry button when applicable, primary match, and score.
- Expanded result card shows all submatches, related/similar items from across the whole corpus with `Relative similarity` percentages, screenshot thumbnail, timestamp links, repository/website links, score components, and processing warnings. Related items render inside the same result container with border color/type variants.
- One video with many matching segments appears as one result, e.g. `12 matches inside`, with best timestamp directly reachable.
- Recent-searches panel with clear-all action.

Verification:

- Manual end-to-end search scenario passes.
- A vague query returns one video cluster with timestamp/repo/website/note matches and related item percentages.

