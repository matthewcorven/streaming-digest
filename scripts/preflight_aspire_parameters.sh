#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)

contract_file=${1:-"$repo_root/src/StreamingDigest.AppHost/required-parameters.localdev.txt"}
apphost_project="$repo_root/src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj"

if [ ! -f "$contract_file" ]; then
  printf 'Contract file not found: %s\n' "$contract_file" >&2
  exit 2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  printf 'dotnet not found in PATH.\n' >&2
  exit 127
fi

secrets_output=$(mktemp)
cleanup() {
  rm -f "$secrets_output"
}
trap cleanup EXIT

if ! dotnet user-secrets list --project "$apphost_project" >"$secrets_output" 2>/dev/null; then
  printf 'Unable to read user secrets for %s\n' "$apphost_project" >&2
  exit 2
fi

found_keys=$(awk -F= '
{
  key = $1
  gsub(/^[[:space:]]+|[[:space:]]+$/, "", key)
  if (length(key) > 0) {
    print key
  }
}' "$secrets_output")

missing_count=0

while IFS= read -r raw_line || [ -n "$raw_line" ]; do
  required_key=$(printf '%s' "$raw_line" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')

  case "$required_key" in
    ''|'#'*)
      continue
      ;;
  esac

  if ! printf '%s\n' "$found_keys" | grep -F -x "$required_key" >/dev/null 2>&1; then
    missing_count=$((missing_count + 1))
    printf 'MISSING_KEY %s\n' "$required_key"
  fi
done <"$contract_file"

if [ "$missing_count" -gt 0 ]; then
  printf 'Missing required AppHost parameter keys (%s).\n' "$missing_count" >&2
  exit 1
fi

printf 'All required AppHost parameter keys are present.\n'