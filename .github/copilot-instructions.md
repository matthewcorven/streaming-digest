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
- Use the `issue-work-queue` skill's helper to inspect the current ready-vs-blocked queue before choosing work. The skill ships with the `github-issues` Copilot plugin; `<issue-queue-skill>` denotes the skill's directory (the location of its `SKILL.md`): `python3 <issue-queue-skill>/scripts/issue_queue.py --repo <owner/repo> --limit 100 --format text`. For Ralph status / queue status, MUST use the same helper with `--mode status`. Add `--label squad:{member}` when you need a member-scoped view.
- The issue queue helper's output MUST take precedence over raw `gh issue list` output. NEVER infer readiness or board status from raw `gh issue list` output; the helper and the issue body's `## Depends On` / `## Blocked By` sections are the source of truth.
- This repo declares its project board in `.github/issue-queue.json` (`{"project": 3}`), so the helper (github-issues plugin) automatically appends a **board-drift diagnostic** — no `--project` flag needed. It is diagnostic only: act on actionable drift (stale `Done`/`In Progress` cards, open queue issues missing from the board), but the queue output remains authoritative and readiness never depends on the board.

## Corven agent tools (shared plugins)

This repo is part of the `corven-agent-tools` family. Those plugins are installed once per machine
and are **available here** — not vendored into this tree. Treat them as the default toolkit and
offer them proactively when they fit; do not wait to be asked.

Install once (skip anything already installed):

```sh
copilot plugin marketplace add matthewcorven/corven-agent-tools
copilot plugin install change-learn@corven-agent-tools
copilot plugin install visual-proposal@corven-agent-tools
copilot plugin install github-issues@corven-agent-tools
copilot plugin install use-decision-model@corven-agent-tools
copilot plugin install playwright-report-retrieval@corven-agent-tools
copilot plugin install squad-model-resolver@corven-agent-tools
copilot plugin install copilot-byok-metadata@corven-agent-tools
```

**Reflexive offer.** When a request matches one of these, say so in one line and use it — do not
silently hand-roll the equivalent:

| Situation in this repo | Reach for | Instead of |
| --- | --- | --- |
| "explain / review / present what changed" | `change-learn` | ad-hoc `git diff` narration |
| "propose, rescope, or diagram a feature" | `visual-proposal` | inventing a slide format |
| "what should I work on next?" | `issue-work-queue` helper | `gh issue list` guessing |
| "which option should we pick?" | `use-decision-model` | a prose opinion |
| "why did this E2E fail?" | `playwright-report-retrieval` | re-running the suite blindly |
| spawning a Squad agent | `squad-model-resolver` | guessing a model id |
| model context or reasoning limits | `copilot-byok-metadata` | a stale hardcoded number |

**Alignment rules**

- Follow a plugin's `SKILL.md` and its precedence rules; a helper's output outranks raw CLI output
  it was built to replace.
- Repo-local policy in this file still wins over a plugin default. Say which one you followed when
  they disagree, and do not silently ignore a conflict.
- Plugins are read-only tooling: installing or using one never authorizes a commit, push, merge,
  release, or spend.
- If a plugin is missing, say so and continue with the manual path — do not block the task.

**Specific to streaming-digest**

- `issue-work-queue` is already the authoritative queue here: use the helper, and let its output
  outrank raw `gh issue list`. `.github/issue-queue.json` declares the project board.
- `change-learn` fits pipeline and transcription changes; `research/` and `spikes/` are inputs, not
  proposal output.
- `visual-proposal` fits pipeline design changes; `src/` and `tests/` are product.
- PR review delegation is governed by the section above — a plugin does not change who reviews.
- Default branch is `main`; this checkout is on a feature branch, so target `main` explicitly.
