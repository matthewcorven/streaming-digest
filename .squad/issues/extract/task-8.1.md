### Task 8.1: Extract description and pinned-comment links

Source: `docs/product/PRD.md` §2.2

Requirements:

- Description links required.
- Pinned comment best-effort.
- Early development decision: use `yt-dlp` for pinned comments if available/reliable; otherwise use public browser scrape where practical.
- Failure to fetch pinned comment is warning only.

Verification:

- Fixture description/comment produces normalized links.
- Pinned-comment failure records warning and does not fail video ingestion.

