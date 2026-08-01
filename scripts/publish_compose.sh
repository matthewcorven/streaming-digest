#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
output_dir="$(mktemp -d)"

if command -v aspire >/dev/null 2>&1; then
  aspire_cmd=("$(command -v aspire)")
elif [[ -x "$HOME/.aspire/bin/aspire" ]]; then
  aspire_cmd=("$HOME/.aspire/bin/aspire")
elif [[ -x "$HOME/.dotnet/tools/aspire" ]]; then
  aspire_cmd=("$HOME/.dotnet/tools/aspire")
else
  printf 'Aspire CLI not found. Install it or add it to PATH, or place it at ~/.aspire/bin/aspire or ~/.dotnet/tools/aspire.\n' >&2
  exit 127
fi

cleanup() {
  rm -rf "$output_dir"
}

trap cleanup EXIT

"${aspire_cmd[@]}" publish \
  --apphost "$repo_root/src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj" \
  --output-path "$output_dir" \
  --non-interactive \
  --nologo \
  "$@"

cp "$output_dir/docker-compose.yaml" "$repo_root/compose.yaml"

printf 'Updated %s/compose.yaml from Aspire AppHost publish output.\n' "$repo_root"