# Docker Image Cleanup Skill

Clean up orphaned, stale, and unused Docker images and containers after local development iterations and git activities (branch switches, rebases, merges) that modify declared Dockerfiles in the current branch.

## When to Use

**Activate this skill when:**
- Local development has finished a feature or branch iteration
- Git activities modified, added, or removed Dockerfiles (detected via `git diff` or `git status`)
- Build artifacts and intermediate container images have accumulated
- Disk space needs reclamation after multiple `docker build` cycles
- Preparing for a fresh build or switching to a different feature branch
- Inspecting Docker state to identify what to safely remove

**Do NOT use for:**
- Removing currently-running containers or images (use `docker stop` / `docker rm` first)
- Production image cleanup (requires separate governance and retention policies)
- Removing images pinned in active docker-compose or Aspire AppHost configurations
- Cleaning volumes holding persistent data (use only images)

## Precedence: Cleanup Phases

Execute cleanup in this priority order to avoid unnecessary removals and maximize safety:

### Phase 1: Dangling Images and Build Cache (Safest)
1. **Dangling images** — untagged, orphaned layers from incomplete builds
2. **Build cache** — unused intermediate layers from `docker build`

```bash
# Remove dangling images only
docker image prune -f

# Remove build cache (safe unless you need instant rebuilds)
docker builder prune -f

# Combined phase 1
docker image prune -f && docker builder prune -f
```

### Phase 2: Stale Build Artifacts (Branch-Specific)
3. **Images from removed Dockerfiles** — tagged images built from Dockerfiles no longer in the branch
4. **Old scraper/builder images** — sequential builds tagged by commit hash or build timestamp
5. **Intermediate platform-specific images** — build-stage images no longer used

Detect using branch context:
```bash
# Identify images built from Dockerfiles in current branch
git ls-files '**Dockerfile*' | sort

# Compare against current Docker images
docker images --format "table {{.Repository}}:{{.Tag}}\t{{.ID}}\t{{.CreatedAt}}"
```

**Removal strategy for phase 2:**
- Keep current `<project>:latest` and `<project>:<branch>` tags
- Remove images with commit hashes or timestamps that predate the current branch's HEAD
- Remove images tagged by previous branch names (e.g., `<image>:old-feature-name`)

```bash
# Example: Remove old scraper build artifacts, keep streaming-digest-scraper:latest
docker images | grep '^scraper:' | grep -v 'latest' | awk '{print $3}' | xargs -r docker rmi -f

# Example: Remove images from deleted Dockerfiles
docker rmi image-name:old-tag -f
```

### Phase 3: Unused Base Images (Selective)
6. **Base images without active children** — sdk, runtime, or os images not referenced by any current image
7. **Version-specific tags** — e.g., `pgvector:pg16` when only `pg18` is used in current Dockerfile

Detect unused base images:
```bash
# Show all images and their dependent children
docker images --format "{{.Repository}}:{{.Tag}}" | xargs -I {} docker history {} 2>/dev/null | grep -v "missing"

# Or use Docker engine API to check image references
docker image inspect <image-id> --format='{{json .RepoTags}}'
```

**Removal strategy for phase 3:**
```bash
# Dry-run: show what would be removed
docker image prune -a --filter "until=72h" --dry-run

# Actual removal of images unused for >72 hours
docker image prune -a --filter "until=72h" -f
```

### Phase 4: Exited Containers (Cleanup-Only, Non-Destructive)
8. **Stopped/exited containers** — from previous `aspire stop` or manual container runs
9. **Dead or orphaned containers** — failed startup attempts

```bash
# Remove all stopped containers
docker container prune -f

# Remove specific dead containers (if any)
docker ps -a --filter "status=dead" -q | xargs -r docker rm -f
```

## Workflow: After Git Activities

Execute this workflow when git activities modify the branch's Dockerfiles:

```bash
# 1. Identify changed Dockerfiles
git diff HEAD~1 -- '**/Dockerfile*' || git status --porcelain -- '**/Dockerfile*'

# 2. List current branch's Dockerfiles
git ls-files '**Dockerfile*'

# 3. Show Docker state before cleanup
docker images
docker system df

# 4. Execute cleanup phases in order
docker image prune -f && docker builder prune -f                    # Phase 1
docker images | grep -E ':<old-commit>|:<old-branch>' | awk '{print $3}' | xargs -r docker rmi -f  # Phase 2 (selective)
docker container prune -f                                           # Phase 4

# 5. Verify cleanup
docker images
docker system df
```

## Repository-Specific Defaults

For `streaming-digest`:

**Current active Dockerfiles:**
- `src/StreamingDigest.Api/Dockerfile`
- `src/StreamingDigest.Worker/Dockerfile`
- `src/StreamingDigest.Scraper/Dockerfile` (JavaScript/Node.js)
- `Dockerfile.whisper` (optional: audio-to-text, MLX on Apple Silicon)

**Keep these image tags (active):**
- `streaming-digest-api:latest`
- `streaming-digest-worker:latest`
- `streaming-digest-scraper:latest`
- `streaming-digest-whisper:latest`

**Safe to remove (build artifacts):**
- `scraper:<commit-hash>` — intermediate builds
- `streaming-digest-*:<old-branch-name>` — feature branch tags
- Unused base image versions:
  - `pgvector:pg16`, `pgvector:pg17` (keep only `pg18-trixie`)
  - `linuxserver/faster-whisper:latest` (replaced by local `streaming-digest-whisper`)
  - `mcr.microsoft.com/dotnet/sdk:*` (runtime-only builds don't need SDK)

**Do NOT remove:**
- `ollama/ollama:latest` (LLM runtime, referenced in AppHost)
- `dpage/pgadmin4:9.6.0` (observability, referenced in compose)
- `grafana/grafana`, `grafana/loki`, `grafana/tempo` (observability stack)
- `prom/prometheus:*`, `otel/opentelemetry-collector-contrib:*` (metrics/traces)

## Safety Guards

1. **Always dry-run before mass removal:**
   ```bash
   docker image prune -a --filter "until=72h" --dry-run
   docker container prune --dry-run
   ```

2. **Verify no running containers use the image:**
   ```bash
   docker ps -a --filter "ancestor=<image-id>" -q  # Check before removing
   ```

3. **Check Aspire AppHost and compose.yaml** for pinned image references before cleanup:
   ```bash
   grep -r "WithImage\|\.WithImageTag\|image:" \
     src/StreamingDigest.AppHost/AppHost.cs \
     compose.yaml
   ```

4. **Keep at least one working `streaming-digest-*:latest` tag per service** to ensure `aspire run` can start.

## Commands by Use Case

**Quick cleanup after feature branch:**
```bash
git ls-files '**Dockerfile*'  # Verify current branch's Dockerfiles
docker image prune -f
docker builder prune -f
docker container prune -f
docker system df
```

**Deep cleanup (reclaim ~15-20GB):**
```bash
# Phase 1
docker image prune -f && docker builder prune -f

# Phase 2: Remove old scraper builds
docker rmi $(docker images | grep '^scraper:' | grep -v latest | awk '{print $3}') -f 2>/dev/null

# Phase 3: Remove unused base images (verify first!)
docker rmi pgvector/pgvector:pg16 pgvector/pgvector:pg17 linuxserver/faster-whisper:latest -f

# Phase 4
docker container prune -f

# Verify
docker system df
```

**Verify impact before deletion:**
```bash
docker system df
docker images --format "{{.Repository}}:{{.Tag}}\t{{.Size}}" | sort -t: -k1,1 -rn
docker ps -a --format "table {{.ID}}\t{{.Status}}\t{{.Image}}"
```
