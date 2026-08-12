## PR Review Delegation

- When a PR review session is created, do **not** begin by reading files, running branch-diff, or performing any review work directly.
- The correct flow is to **immediately delegate** to Morpheus (or the designated code reviewer on the team roster) as the first action — before any file reads or diff analysis.
- The designated reviewer — not the PR review session shell — is responsible for branch-diff, all file reads, and all review analysis.
- A PR review session that reads files or inspects diffs before delegating violates this protocol.

## Agent Tool Use

- When asking questions of the human operator, use the ask_questions/askQuestions tool.

## Application Code/Script Development: Debug Logging Rule

- applies to all application code and scripts in this repository, agnostic to language, framework, or runtime
- Every conditional path logs at `Debug`: each `if` body, each `else` body, and every `try`/`catch`/`finally` path. Keep logs short, structured, and searchable so Aspire logs and file-based searches can trace branch execution.
- All test coverage must include log verification for each conditional path.

## Issue-driven work queue

- GitHub issues are the authoritative execution queue for implementation work in this repository.
- When deciding what to work on next, check the issue bodies and use the `## Depends On` and `## Blocked By` sections as the source of truth for readiness and blockers.
- Do not infer readiness from issue titles or from a retired task manifest.
- Use `python3 scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text` to inspect the current ready-vs-blocked queue before choosing work. For Ralph status / queue status, MUST use `python3 scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text --mode status`. Add `--label squad:{member}` when you need a member-scoped view.
- NEVER infer readiness or board status from raw `gh issue list` output; the issue helper and the issue body's `## Depends On` / `## Blocked By` sections are the source of truth.
