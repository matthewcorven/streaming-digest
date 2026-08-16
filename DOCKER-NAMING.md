# Docker Image Naming Convention

This document defines the standardized naming convention for Docker images in the streaming-digest project.

## Project Images

All images built and managed by this project follow this naming convention:

```
ghcr.io/matthewcorven/streaming-digest-{service}:{tag}
```

### Components

- **Registry**: `ghcr.io` (GitHub Container Registry)
- **Namespace**: `matthewcorven/streaming-digest`
- **Service Name**: Lowercase service identifier (e.g., `api`, `worker`, `scraper`, `whisper`)
- **Tag**: Version identifier (see [Tagging Strategy](#tagging-strategy))

### Examples

- `ghcr.io/matthewcorven/streaming-digest-api:latest`
- `ghcr.io/matthewcorven/streaming-digest-worker:v1.2.0`
- `ghcr.io/matthewcorven/streaming-digest-scraper:1.2.0-abc1234`

## Project Services

| Service | Image | Dockerfile | Environment Variable |
|---------|-------|------------|-----------------------|
| API | `ghcr.io/matthewcorven/streaming-digest-api` | `src/StreamingDigest.Api/Dockerfile` | `API_IMAGE` |
| Worker | `ghcr.io/matthewcorven/streaming-digest-worker` | `src/StreamingDigest.Worker/Dockerfile` | `WORKER_IMAGE` |
| Scraper | `ghcr.io/matthewcorven/streaming-digest-scraper` | `src/StreamingDigest.Scraper/Dockerfile` | `SCRAPER_IMAGE` |
| Whisper | `ghcr.io/matthewcorven/streaming-digest-whisper` | `Dockerfile.whisper` | `WHISPER_IMAGE` |
| Web | Variable (Aspire) | N/A | `WEB_IMAGE` |

## External Images

External images from third-party registries must use **full registry paths** to avoid ambiguity:

### Registry Rules

- **Docker Hub**: Use explicit `docker.io/` prefix
  - ✓ `docker.io/pgvector/pgvector:0.8.5-pg18-trixie`
  - ✗ `pgvector/pgvector:0.8.5-pg18-trixie` (ambiguous without prefix)

- **Official Registries**: Use service-provided URLs when available
  - `registry.grafana.com/grafana/grafana:11.4.0`
  - `registry.ollama.ai/ollama:latest`

- **Version Pinning**: Always pin exact versions for production stability
  - ✓ `docker.io/ollama/ollama:0.4.0`
  - ✗ `docker.io/ollama/ollama:latest` (for production)

### Current External Services

| Service | Image | Usage |
|---------|-------|-------|
| PostgreSQL + pgvector | `docker.io/pgvector/pgvector:0.8.5-pg18-trixie` | Database with vector support |
| Ollama | `docker.io/ollama/ollama:latest` | LLM inference engine |
| OpenTelemetry Collector | `otel/opentelemetry-collector-contrib:0.114.0` | Observability pipeline |
| Prometheus | `prom/prometheus:v2.54.0` | Metrics collection |
| Grafana | `grafana/grafana:11.4.0` | Metrics visualization |
| pgAdmin | `dpage/pgadmin4:9.6.0` | Database management UI |
| Loki | `grafana/loki:3.2.0` | Log aggregation |
| Tempo | `grafana/tempo:2.6.0` | Distributed tracing |

## Tagging Strategy

### Development (Local, PR, dev branch)

```
{MAJOR}.{MINOR}.{PATCH}-{git-short-sha}
```

Example: `1.2.0-abc1234`

- Represents a development build from a specific commit
- Automatically generated in CI
- Ephemeral — not retained long-term

### Stable (Latest Development Build)

```
latest
```

- Tracks the most recent development build
- Used by default in compose.yaml for local development
- Suitable for ephemeral environments

### Release (Preview and Main branches)

```
{MAJOR}.{MINOR}.{PATCH}
```

Example: `1.2.0`

- Created on releases
- Stable for production deployments
- Never overwritten once published

### Branch-Specific (Optional, for testing)

```
{branch-name}
```

Example: `feat-embeddings`, `main`, `preview`

- Useful for testing specific feature branches
- Not recommended for production

## Configuration in compose.yaml

Project images are configured via environment variables to support local overrides:

```yaml
services:
  api:
    image: "${API_IMAGE:-ghcr.io/matthewcorven/streaming-digest-api:latest}"
    # ... rest of config
```

### Default .env Values

```env
# Default to latest development image from registry
API_IMAGE=ghcr.io/matthewcorven/streaming-digest-api:latest
WORKER_IMAGE=ghcr.io/matthewcorven/streaming-digest-worker:latest
SCRAPER_IMAGE=ghcr.io/matthewcorven/streaming-digest-scraper:latest
WHISPER_IMAGE=ghcr.io/matthewcorven/streaming-digest-whisper:latest
```

## Local Development

### Using Registry Images

```bash
# Use defaults from compose.yaml (pulls from ghcr.io)
docker-compose up
```

### Using Local Builds

Override in `.env` to use locally-built images:

```bash
# Build locally
docker build -f src/StreamingDigest.Api/Dockerfile -t my-streaming-api:dev .

# Override in .env
echo "API_IMAGE=my-streaming-api:dev" >> .env

# Run
docker-compose up
```

### Rebuild and Test

```bash
# Rebuild all project images locally
docker-compose build

# Run with local builds
docker-compose up
```

## CI/CD Integration

### Build Triggers

- **Push to dev**: Build and tag as `{sha-short}`, push `latest`
- **Push to preview**: Build and tag as `{version}-preview`, push to registry
- **Push to main**: Build and tag as `{version}`, push to registry
- **Pull requests**: Build for verification, do not push

### Registry Authentication

GitHub Actions uses the built-in `GITHUB_TOKEN` to authenticate with GitHub Container Registry (ghcr.io). No additional secrets needed for public images.

For private registry access, add:
- `GHCR_USERNAME`: GitHub username
- `GHCR_TOKEN`: GitHub personal access token with `write:packages` scope

## Pull Policy

In production Kubernetes/container orchestration:

```yaml
imagePullPolicy: IfNotPresent  # For tagged releases
imagePullPolicy: Always        # For :latest tags
```

Local Docker Compose uses the default policy (pull if not present locally).

## Migration Notes

### From Old Pattern to New Pattern

| Old | New | Migration |
|-----|-----|-----------|
| `streaming-digest-api` | `ghcr.io/matthewcorven/streaming-digest-api:latest` | Use env var override or rebuild locally |
| `streaming-digest-worker` | `ghcr.io/matthewcorven/streaming-digest-worker:latest` | Use env var override or rebuild locally |
| `streaming-digest-scraper` | `ghcr.io/matthewcorven/streaming-digest-scraper:latest` | Use env var override or rebuild locally |
| `streaming-digest-whisper:latest` | `ghcr.io/matthewcorven/streaming-digest-whisper:latest` | Use env var override or rebuild locally |

**No breaking changes**: Local development continues to work. Override `*_IMAGE` env vars to use locally-built images.

## Troubleshooting

### "image not found" error

1. Check if running with registry images (default):
   ```bash
   # Ensure ghcr.io authentication is set up
   docker login ghcr.io
   ```

2. Or override to use local builds:
   ```bash
   # Build locally and update .env
   docker-compose build
   API_IMAGE=streaming-digest-api:latest docker-compose up
   ```

### Image pull timeout

- Registry may be slow or unavailable
- Override to use local builds: `docker-compose build && docker-compose up`

### Invalid image reference

- Verify `{*_IMAGE}` env vars are set in `.env` or environment
- Check syntax: `registry/namespace/image:tag`

## References

- [GitHub Container Registry Documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [Docker Official Image Naming Guide](https://docs.docker.com/reference/cli/docker/image/tag/#description)
- [Kubernetes Image Pull Policy](https://kubernetes.io/docs/concepts/containers/images/#image-pull-policy)
