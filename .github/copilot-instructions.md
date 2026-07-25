## Agent Tool Use

- When asking questions of the human operator, use the ask_questions/askQuestions tool.

## Issue-driven work queue

- GitHub issues are the authoritative execution queue for implementation work in this repository.
- When deciding what to work on next, check the issue bodies and use the `## Depends On` and `## Blocked By` sections as the source of truth for readiness and blockers.
- Do not infer readiness from issue titles or from a retired task manifest.
- Use `python3 scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text` to inspect the current ready-vs-blocked queue before choosing work. For Ralph status / queue status, MUST use `python3 scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text --mode status`. Add `--label squad:{member}` when you need a member-scoped view.
- NEVER infer readiness or board status from raw `gh issue list` output; the issue helper and the issue body's `## Depends On` / `## Blocked By` sections are the source of truth.
