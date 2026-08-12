#!/usr/bin/env sh
set -eu

phase="manual"
if [ "$#" -gt 0 ]; then
  case "$1" in
    --phase)
      phase="${2:-manual}"
      ;;
    *)
      phase="$1"
      ;;
  esac
fi

printf '[scraper-prune] phase=%s\n' "$phase"

docker_bin="${DOCKER:-docker}"

if ! command -v "$docker_bin" >/dev/null 2>&1; then
  printf '[scraper-prune] Docker CLI not found; skipping cleanup.\n'
  exit 0
fi

if ! "$docker_bin" info >/dev/null 2>&1; then
  printf '[scraper-prune] Docker daemon is not available; skipping cleanup.\n'
  exit 0
fi

matched_containers=$($docker_bin ps -aq --filter "name=streaming-digest-scraper" || true)
if [ -n "$matched_containers" ]; then
  printf '[scraper-prune] Removing stale scraper containers:\n%s\n' "$matched_containers"
  # shellcheck disable=SC2086
  $docker_bin rm -f $matched_containers >/dev/null
else
  printf '[scraper-prune] No stale scraper containers found.\n'
fi

printf '[scraper-prune] Pruning unused Docker containers and images.\n'
$docker_bin container prune -f >/dev/null
$docker_bin image prune -f >/dev/null

printf '[scraper-prune] Cleanup complete.\n'
