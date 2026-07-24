### Task 9.4: Check DeepWiki URL

Source: `docs/architecture/ARCHITECTURE.md` §9 (DeepWiki detection)

For repo owner/name:

- Build `https://deepwiki.com/{owner}/{repo}`.
- Fetch page.
- Store URL only if reachable and not placeholder text such as "Index your code".
- DeepWiki is a host scope like any other: a 429 defers all remaining checks in the run rather than failing them. Outcome is write-once, except negative outcomes (no page/placeholder) re-check on Repository Reprocess; a stored reachable URL is never re-verified in MVP.

Verification:

- Placeholder page is rejected.
- Existing fixture is accepted.
- 429 fixture defers remaining checks; negative outcome re-checks on Reprocess.

## Phase 10: Website scraping

