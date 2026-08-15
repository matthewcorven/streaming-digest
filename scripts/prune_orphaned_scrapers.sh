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

# Remove stale scraper containers
matched_containers=$($docker_bin ps -aq --filter "name=streaming-digest-scraper" || true)
if [ -n "$matched_containers" ]; then
  printf '[scraper-prune] Removing stale scraper containers:\n%s\n' "$matched_containers"
  # shellcheck disable=SC2086
  $docker_bin rm -f $matched_containers >/dev/null
else
  printf '[scraper-prune] No stale scraper containers found.\n'
fi

# Remove old commit-hash-tagged images (keep only the most recent 3)
for image_name in scraper streaming-digest-api streaming-digest-worker streaming-digest-whisper; do
  printf '[scraper-prune] Pruning old %s images (keeping 3 most recent)...\n' "$image_name"
  
  # Get images and their creation times, sorted newest first
  old_images=$($docker_bin images --format "table {{.Repository}}:{{.Tag}}\t{{.CreatedAt}}" --filter "reference=${image_name}*" 2>/dev/null || true)
  
  # If more than 3 images exist, remove the oldest ones
  if [ -n "$old_images" ]; then
    image_count=$(printf '%s' "$old_images" | tail -n +2 | wc -l)
    if [ "$image_count" -gt 3 ]; then
      # Sort by date (descending), skip header and first 3 lines, extract image names
      printf '%s' "$old_images" | tail -n +2 | sort -k2 -r | tail -n +4 | awk '{print $1}' | while read -r img; do
        printf '[scraper-prune] Removing old image: %s\n' "$img"
        $docker_bin rmi "$img" >/dev/null 2>&1 || true
      done
    fi
  fi
done

printf '[scraper-prune] Pruning unused Docker containers and images.\n'
$docker_bin container prune -f >/dev/null
$docker_bin image prune -f >/dev/null

printf '[scraper-prune] Cleanup complete.\n'
