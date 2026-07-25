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
8. **Sequential execution is mandatory for implementation-plan work.** Agents must execute GitHub issues according to the dependency graph in `.squad/task-manifest.json` rather than by recency, issue number, or current backlog position.
9. **Stop on blockers.** If a task is blocked by an unmet prerequisite, missing dependency, or unresolved blocker, agents must stop and report the blocker rather than jumping ahead to later tasks.
10. **Blocked work stays open.** A task that is blocked remains open and does not get marked complete or implicitly skipped.
11. **Manifest-first orchestration.** When the task manifest changes, agents must re-read `.squad/task-manifest.json` before choosing work so new tasks and re-ordered dependencies are respected.
