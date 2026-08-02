# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
| --- | --- | --- |
| Architecture and scope | Morpheus | Cross-cutting design, interfaces, priorities, technical decisions |
| Frontend and UX | Trinity | Blazor WASM search flows, admin UI, interaction design |
| Backend and ingestion | Tank | REST API, jobs, YouTube ingestion, enrichment pipelines |
| Platform and orchestration | Dozer | Aspire app host, Docker Compose generation, local environment wiring |
| Data, search, and AI | Neo | PostgreSQL, pgvector, embeddings, ranking, enrichment heuristics |
| Code review | Morpheus | Review PRs, check quality, suggest improvements |
| Testing | Switch | Write tests, find edge cases, verify fixes |
| Scope & priorities | Morpheus | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic - never needs routing |

## Issue Routing

| Label | Action | Who |
| --- | --- | --- |
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
8. **Dependency-driven issue execution.** Before choosing work, agents MUST run `python3 scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text` to identify the first open issue with no unmet dependencies or blockers and inspect the `Available`/`Blocked` sections. Add `--label squad:{member}` when they need a member-scoped queue. Agents MUST treat the issue bodies' `## Depends On` and `## Blocked By` sections as the source of truth for ordering; the issue title is not used for readiness or prioritization.
9. **Stop on blockers.** If a task is blocked by an unmet prerequisite, missing dependency, or unresolved blocker, agents must stop and report the blocker rather than jumping ahead to later tasks.
10. **Blocked work stays open.** A task that is blocked remains open and does not get marked complete or implicitly skipped.
11. **Issue tracker is the live queue.** When issue content changes, agents must re-run the helper before choosing work so the latest dependencies and blockers are respected.
12. **Ralph owns the queue.** For "Ralph, status", "what's on the board?", "queue status", and similar requests, Ralph MUST run `python3 scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text --mode status` first and share that helper output with the squad. Ralph MUST NEVER infer readiness or board state from raw `gh issue list` output or ad hoc label scanning; raw GitHub queries are follow-up only after the helper identifies the item to inspect.
13. **Ralph delegates via sub-sessions.** In Copilot App mode, Ralph MUST launch issue implementation, adversarial review, and revision follow-up work with `create_session`. Ralph does not implement inline and uses `task` only for fallback or ephemeral analysis that does not need a reply path.
14. **Mandatory adversarial review pass.** When an issue worker believes work is complete, the worker yields back to the coordinator and requests independent adversarial review. At least one review iteration is required before the issue can be treated as complete or ready for final review.
15. **Raw review-artifact handoff.** The adversarial reviewer must produce `completeness_after_any_fixes.txt` and `review_change_specifications.md`. The coordinator may read/report the completeness file, but MUST NOT read, paraphrase, or distill `review_change_specifications.md`; it forwards the absolute file path directly to the requesting worker session.
