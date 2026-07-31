#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
output_dir="$(mktemp -d)"

cleanup() {
  rm -rf "$output_dir"
}

trap cleanup EXIT

aspire publish \
  --apphost "$repo_root/src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj" \
  --output-path "$output_dir" \
  --non-interactive \
  --nologo \
  "$@"

cp "$output_dir/docker-compose.yaml" "$repo_root/compose.yaml"

printf 'Updated %s/compose.yaml from Aspire AppHost publish output.\n' "$repo_root"