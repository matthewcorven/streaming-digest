### Task 11.6: Implement recent-search storage and embeddings

Source: `docs/architecture/DATA_MODEL.md` §3.24–3.26; `docs/product/PRD.md` §2.3

Requirements:

- Store recent searches in PostgreSQL.
- Embed each search using the active embedding model.
- Store query text, searched_at, active text/vector weights, filters JSON, and embedding reference.
- Store MVP interaction events for clicked/opened results so they can boost future signal strength.
- Provide a recent-searches panel and a clear-all search-history action.
- Granular per-query deletion is MVP+.

Verification:

- Search query creates a recent-search row and embedding.
- Clear-all removes recent-search history.
- Opened result creates a user-signal event used by high-signal ranking.

