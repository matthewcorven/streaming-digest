#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
docker_bin="${DOCKER:-docker}"

if ! command -v "$docker_bin" >/dev/null 2>&1; then
  printf 'docker not found in PATH.\n' >&2
  exit 127
fi

if ! "$docker_bin" info >/dev/null 2>&1; then
  printf 'docker daemon is not available.\n' >&2
  exit 1
fi

printf '[reset-local-state] stopping Aspire sessions if present\n'
if command -v aspire >/dev/null 2>&1; then
  (cd "$repo_root" && aspire stop >/dev/null 2>&1 || true)
fi

printf '[reset-local-state] stopping local repo processes\n'
pkill -f '/Users/core/git/matthewcorven/streaming-digest/src/StreamingDigest.AppHost/bin/Debug/net10.0/StreamingDigest.AppHost' 2>/dev/null || true
pkill -f '/Users/core/git/matthewcorven/streaming-digest/src/StreamingDigest.Api/bin/Debug/net10.0/StreamingDigest.Api.dll' 2>/dev/null || true
pkill -f 'blazor-devserver.dll --applicationpath /Users/core/git/matthewcorven/streaming-digest/src/StreamingDigest.Web/bin/Debug/net10.0/StreamingDigest.Web.dll' 2>/dev/null || true

printf '[reset-local-state] removing streaming-digest containers\n'
containers=$($docker_bin ps -aq --filter "name=streaming-digest-" --filter "name=postgres-" --filter "name=pgadmin-" || true)
if [ -n "$containers" ]; then
  # shellcheck disable=SC2086
  $docker_bin rm -f $containers >/dev/null
fi

printf '[reset-local-state] removing streaming-digest volumes\n'
volumes=$($docker_bin volume ls -q | grep '^streamingdigest-' || true)
if [ -n "$volumes" ]; then
  # shellcheck disable=SC2086
  $docker_bin volume rm -f $volumes >/dev/null
fi

printf '[reset-local-state] pruning dangling containers and images\n'
$docker_bin container prune -f >/dev/null
$docker_bin image prune -f >/dev/null

printf '[reset-local-state] local state reset complete\n'