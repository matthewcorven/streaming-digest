#!/usr/bin/env python3
import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from typing import Dict, List, Optional


def run_gh(args: List[str]) -> str:
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip() or "gh command failed")
    return result.stdout


def get_repo_name() -> str:
    try:
        return run_gh(["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"]).strip()
    except RuntimeError:
        return "matthewcorven/streaming-digest"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Discover the next GitHub issues that are ready to work on based on their issue-body dependency sections.")
    parser.add_argument("--repo", default=None, help="GitHub repository (owner/repo). Defaults to the current repo.")
    parser.add_argument("--state", default="open", choices=["open", "all"], help="Issue state to inspect (default: open).")
    parser.add_argument("--label", action="append", default=None, help="Label to include. Repeat for multiple labels. Defaults to squad.")
    parser.add_argument("--format", choices=["json", "text"], default="json", help="Output format (default: json).")
    parser.add_argument("--limit", type=int, default=1000, help="Maximum issues to fetch from GitHub (default: 1000).")
    return parser.parse_args()


def normalize_eol(value: str) -> str:
    return (value or "").replace("\r\n", "\n").replace("\r", "\n")


def parse_task_id(title: str) -> Optional[str]:
    match = re.search(r"\[Task\s+([0-9]+(?:\.[0-9]+)*(?:[a-z])?)\]", title or "", re.IGNORECASE)
    return match.group(1) if match else None


def extract_section_body(body: str, heading: str) -> str:
    lines = normalize_eol(body or "").split("\n")
    in_section = False
    section_lines: List[str] = []

    for line in lines:
        trimmed = line.strip()
        if not in_section:
            match = re.match(r"^#{2,3}\s+(.+)$", trimmed)
            if match and match.group(1).strip().lower() == heading.lower():
                in_section = True
            continue

        if re.match(r"^#{2,3}\s+", trimmed):
            break

        section_lines.append(line)

    return "\n".join(section_lines).strip()


def parse_section_references(body: str, heading: str) -> List[str]:
    section_body = extract_section_body(body, heading)
    if not section_body:
        return []

    references: List[str] = []
    token_pattern = re.compile(r"(?<![\w.])(?:#)?([0-9]+(?:\.[0-9]+)*(?:[a-z])?)(?![\w.])")
    for raw_line in section_body.splitlines():
        line = raw_line.strip()
        if not line or re.match(r"^(none|n/a)$", line, re.IGNORECASE):
            continue

        cleaned = re.sub(r"^[-*]\s*", "", line)
        cleaned = cleaned.replace("`", "").strip()
        if not cleaned or re.match(r"^(none|n/a)$", cleaned, re.IGNORECASE):
            continue

        for match in token_pattern.finditer(cleaned):
            references.append(match.group(1))

    return references


def has_label(issue: Dict[str, object], label_name: str) -> bool:
    labels = issue.get("labels", []) or []
    return any((label.get("name") or "").lower() == label_name.lower() for label in labels)


def build_issue_index(issues: List[Dict[str, object]]) -> Dict[str, Dict[str, object]]:
    by_task_id: Dict[str, Dict[str, object]] = {}
    by_number: Dict[int, Dict[str, object]] = {}
    for issue in issues:
        number = int(issue["number"])
        by_number[number] = issue
        task_id = parse_task_id(str(issue.get("title") or ""))
        if task_id:
            by_task_id[task_id] = issue
    return {"by_task_id": by_task_id, "by_number": by_number}


def resolve_reference(reference: str, issue_index: Dict[str, Dict[str, object]]) -> Optional[Dict[str, object]]:
    normalized = reference.strip()
    if not normalized:
        return None

    if "." in normalized:
        return issue_index["by_task_id"].get(normalized)

    try:
        issue_number = int(normalized)
    except ValueError:
        return None

    return issue_index["by_number"].get(issue_number)


def build_issue_summaries(issues: List[Dict[str, object]], issue_index: Dict[str, Dict[str, object]]) -> List[Dict[str, object]]:
    summaries: List[Dict[str, object]] = []
    for issue in issues:
        title = str(issue.get("title") or "")
        body = str(issue.get("body") or "")
        task_id = parse_task_id(title)
        # Readiness is driven by the issue body sections, not the title.
        # "Depends On" and "Blocked By" are the source of truth for ordering.
        depends_on = parse_section_references(body, "Depends On")
        blocked_by = parse_section_references(body, "Blocked By")

        unresolved_dependencies = []
        for reference in depends_on:
            resolved = resolve_reference(reference, issue_index)
            if not resolved or resolved.get("state") == "OPEN":
                unresolved_dependencies.append({"reference": reference, "resolved": resolved, "missing": resolved is None})

        unresolved_blockers = []
        for reference in blocked_by:
            resolved = resolve_reference(reference, issue_index)
            if not resolved or resolved.get("state") == "OPEN":
                unresolved_blockers.append({"reference": reference, "resolved": resolved, "missing": resolved is None})

        summaries.append(
            {
                "number": int(issue["number"]),
                "task_id": task_id,
                "title": title,
                "state": issue.get("state"),
                "depends_on": depends_on,
                "blocked_by": blocked_by,
                "unresolved_dependencies": unresolved_dependencies,
                "unresolved_blockers": unresolved_blockers,
                "is_blocked": bool(unresolved_dependencies or unresolved_blockers),
            }
        )

    summaries.sort(key=lambda item: int(item["number"]))
    return summaries


def format_text(result: Dict[str, object]) -> str:
    lines = [f"Repository: {result['repo']}", f"State: {result['state']}", ""]

    if result.get("next_available"):
        lines.append("Next available:")
        lines.append(f"- #{result['next_available']['number']} {result['next_available']['title']}")
        lines.append("")
    else:
        lines.append("Next available: none")
        lines.append("")

    lines.append("Available:")
    available = result.get("available") or []
    if available:
        for issue in available:
            lines.append(f"- #{issue['number']} {issue['title']}")
    else:
        lines.append("- none")

    lines.append("")
    lines.append("Blocked:")
    blocked = result.get("blocked") or []
    if blocked:
        for issue in blocked:
            reasons = []
            if issue.get("unresolved_dependencies"):
                deps = ", ".join(
                    f"#{entry['resolved']['number']}" if entry.get("resolved") else entry["reference"]
                    for entry in issue["unresolved_dependencies"]
                )
                reasons.append(f"depends on {deps}")
            if issue.get("unresolved_blockers"):
                blockers = ", ".join(
                    f"#{entry['resolved']['number']}" if entry.get("resolved") else entry["reference"]
                    for entry in issue["unresolved_blockers"]
                )
                reasons.append(f"blocked by {blockers}")
            lines.append(f"- #{issue['number']} {issue['title']} ({'; '.join(reasons)})")
    else:
        lines.append("- none")

    return "\n".join(lines)


def main() -> int:
    args = parse_args()
    repo = args.repo or get_repo_name()
    labels = [label for label in (args.label or ["squad"]) if label]

    command_args = [
        "issue",
        "list",
        "--repo",
        repo,
        "--state",
        args.state,
        "--limit",
        str(args.limit),
        "--json",
        "number,title,body,state,labels",
    ]
    stdout = run_gh(command_args)
    issues = json.loads(stdout)

    filtered_issues = [issue for issue in issues if not issue.get("pull_request")]
    if labels:
        filtered_issues = [issue for issue in filtered_issues if all(has_label(issue, label) for label in labels)]

    issue_index = build_issue_index(filtered_issues)
    summaries = build_issue_summaries(filtered_issues, issue_index)
    available = [issue for issue in summaries if issue["state"] == "OPEN" and not issue["is_blocked"]]
    blocked = [issue for issue in summaries if issue["state"] == "OPEN" and issue["is_blocked"]]

    result = {
        "repo": repo,
        "state": args.state,
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "next_available": available[0] if available else None,
        "available": available,
        "blocked": blocked,
    }

    if args.format == "json":
        print(json.dumps(result, indent=2))
    else:
        print(format_text(result))

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # pragma: no cover - simple CLI wrapper
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1)
