# Aspire AppHost → Docker Compose Publication Workflow

## Overview

This guide explains how Streaming Digest uses **Aspire AppHost** to define infrastructure and generates a **Docker Compose** artifact for production deployment. The AppHost is the source of truth; `compose.yaml` is a generated artifact that should be regenerated whenever the AppHost changes.

## Quick Start

### For local development (Aspire orchestration)
```bash
dotnet run --project src/StreamingDigest.AppHost
```

### For production (Docker Compose)
```bash
# 1. Ensure AppHost is up-to-date
# 2. Regenerate compose.yaml
./scripts/publish_compose.sh

# 3. Copy environment template and fill in secrets
cp .env.example .env
# Edit .env with your PostgreSQL credentials, API keys, etc.

# 4. Start the stack
docker compose up -d

# 5. Check logs
docker compose logs -f streaming-digest-api
```

## Architecture: AppHost → Compose → Deployment

```
┌─────────────────────────────────┐
│   Aspire AppHost (C#)           │  Source of truth
│   src/StreamingDigest.AppHost   │  • Infrastructure definitions
│   • Container definitions       │  • Service dependencies
│   • Network topology            │  • Port mappings
│   • Environment variables       │  • Volume definitions
│   • Secret/parameter references │  • Orchestration rules
└──────────────┬──────────────────┘
               │
               │ aspire publish
               ↓
┌─────────────────────────────────┐
│  docker-compose.yaml            │  Generated artifact
│  • Declarative services         │  • Checksummed (git-tracked)
│  • Environment refs ($VAR)      │  • Manual edits risk breakage
│  • Bind mounts + volumes        │  • Regenerate on AppHost change
│  • Networking and depends_on    │
└──────────────┬──────────────────┘
               │
               │ docker compose up -d
               ↓
┌─────────────────────────────────┐
│  Running Container Stack        │  Live deployment
│  • Postgres + pgvector          │  • Volumes persist across restarts
│  • API + Web services           │  • Networks isolate internal traffic
│  • Ollama + Whisper             │  • Env vars override at runtime
│  • Observability stack          │  • Logs/metrics collected
└─────────────────────────────────┘
```

## Publishing Compose from AppHost

### The publish_compose.sh Script

Located at `./scripts/publish_compose.sh`, this script automates Compose generation:

```bash
#!/bin/env bash
# Finds the Aspire CLI (from PATH, ~/.aspire/bin/, or ~/.dotnet/tools/)
# Runs: aspire publish --apphost <project> --output-path <temp> --non-interactive
# Copies the generated docker-compose.yaml to ./compose.yaml
```

**Why a script?**
- Provides a consistent interface across dev/CI/CD environments
- Hides Aspire CLI discovery logic (installed in multiple locations)
- Ensures `--non-interactive` mode (no prompts in CI)
- Outputs a human-readable message on completion

### Prerequisites

#### Install Aspire CLI

```bash
# Option 1: Global installation (recommended)
dotnet tool install --global Aspire.Cli

# Option 2: User-local installation
dotnet tool install --local Aspire.Cli
export PATH="$HOME/.dotnet/tools:$PATH"

# Option 3: Manual placement
mkdir -p ~/.aspire/bin
# Place the aspire binary at ~/.aspire/bin/aspire
```

**Verify installation:**
```bash
aspire --version
```

### Running the Script

From the repository root:

```bash
# Generate compose.yaml from the current AppHost
./scripts/publish_compose.sh

# You can also pass additional flags to aspire publish
./scripts/publish_compose.sh --target docker
```

**Output:**
```
Updated ./compose.yaml from Aspire AppHost publish output.
```

### What Gets Generated

The `compose.yaml` artifact includes:

#### Service Definitions
- **postgres**: pgvector-enabled PostgreSQL 18
- **ollama**: Local large language model runtime
- **ollama-bootstrap**: Initializes Ollama with required models (bge-m3, llama3.1:8b)
- **whisper**: Audio-to-text service (optional; source-built)
- **api**: .NET API service (runtime, routes)
- **worker**: Background job processor (Hangfire orchestration)
- **web**: Blazor frontend
- **otel-collector**: OpenTelemetry trace/metric collector
- **prometheus**: Metrics storage and query engine
- **grafana**: Metrics visualization dashboard
- **loki**: Log aggregation (optional)
- **tempo**: Distributed trace storage (optional)

#### Networking
- Single `aspire` network (internal-only for non-exposed services)
- Port mappings for external services (API on 8000, Web on 8001, Grafana on 3000, etc.)
- Service-to-service DNS (e.g., `postgres:5432` resolves within the network)

#### Volumes
- **postgres18-data**: Persists Postgres state across restarts
- **ollama-data**: Caches downloaded LLM models
- **prometheus-data**: Metrics history
- Bind mounts for config files (Prometheus, Grafana, OpenTelemetry)

#### Environment Variables
Compose references `.env` file variables:
```yaml
services:
  postgres:
    environment:
      POSTGRES_USER: "${POSTGRES_USERNAME}"
      POSTGRES_PASSWORD: "${POSTGRES_PASSWORD}"
```

These are **NOT** hardcoded; they're injected at startup from `.env` (see "Environment Externalization" below).

## Environment Externalization

### What Are Bind Mount Variables?

Bind mount environment variables like `${OTEL_COLLECTOR_BINDMOUNT_0}` tell Docker Compose where on the host filesystem to find configuration files:

```yaml
otel-collector:
  volumes:
    - type: "bind"
      target: "/etc/otelcol-contrib/config.yaml"      # Inside container
      source: "${OTEL_COLLECTOR_BINDMOUNT_0}"         # Host path (from .env)
      read_only: true
```

### Providing Runtime Values

Create `.env` in the repository root:

```bash
cp .env.example .env
```

Then edit `.env` with your environment:

```env
# Database credentials
POSTGRES_USERNAME=streaming_user
POSTGRES_PASSWORD=your-secure-password-here

# Admin credentials
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=your-grafana-password

# Bind mount paths (host filesystem)
OTEL_COLLECTOR_BINDMOUNT_0=/path/to/otel-collector-config.yaml
PROMETHEUS_BINDMOUNT_0=/path/to/prometheus.yml
GRAFANA_BINDMOUNT_0=/path/to/grafana/provisioning/dashboards
GRAFANA_BINDMOUNT_1=/path/to/grafana/provisioning/datasources
LOKI_BINDMOUNT_0=/path/to/loki-config.yaml
TEMPO_BINDMOUNT_0=/path/to/tempo-config.yaml
```

**Important**: Never commit `.env` (it contains secrets). The `.gitignore` excludes it automatically.

### .env vs .env.example

- **`.env.example`**: Template with all required variables and documentation. Committed to the repo. Users copy this to `.env` and fill in their values.
- **`.env`**: Actual runtime values (credentials, paths, etc.). **NOT** committed; ignored by `.gitignore`.

When AppHost changes add new environment variables, update `.env.example` and ask users to sync their `.env` with the new template.

## Managing Compose Changes

### When to Regenerate vs Hand-Edit

| Scenario | Action | Reason |
|----------|--------|--------|
| AppHost code changes (new services, port changes) | **Regenerate** with `./scripts/publish_compose.sh` | AppHost is source of truth; manual edits get overwritten |
| Need to adjust a port or volume path | Check if AppHost defines it; if yes, change AppHost + regenerate | Keeps AppHost and Compose in sync |
| Want to add a custom sidecar service | Hand-edit `compose.yaml` + document it | Out of AppHost scope; manually maintained |
| Environment variable defaults change | Hand-edit `.env.example` only | Doesn't affect generated artifact |

**Rule of thumb**: If it's in the AppHost, regenerate. If it's custom infrastructure outside the AppHost (your sidecar, your monitoring), hand-edit.

### Preserving Custom Compose Edits

If you've hand-edited `compose.yaml` (e.g., added a custom service) and later regenerate:

#### Before regenerating:
```bash
# Save your custom edits
cp compose.yaml compose.yaml.bak

# Regenerate from AppHost
./scripts/publish_compose.sh

# Compare and reapply custom changes
diff compose.yaml.bak compose.yaml
# Manually add back any custom services from the .bak file
```

#### Better approach: Extend via `docker-compose.override.yml`

Docker Compose supports override files. Create `.docker-compose.override.yml`:

```yaml
# This file is NOT regenerated by publish_compose.sh
# Use it for custom services or overrides that survive regeneration

services:
  my-custom-service:
    image: my-custom-app:latest
    ports:
      - "9999:9999"
    networks:
      - "aspire"
```

Then run:
```bash
docker compose -f compose.yaml -f docker-compose.override.yml up -d
```

The override file survives regeneration and extends the base Compose without duplication.

## Version and Schema Alignment

### Compose Specification

The generated `compose.yaml` uses Docker Compose v3.8+ specification:

```yaml
version: "3.8"  # Or auto-detected if omitted
services: { ... }
volumes: { ... }
networks: { ... }
```

**Schema compatibility**: The AppHost publishes for Docker Compose, which means:
- Compatible with `docker compose` CLI (v2.0+)
- Compatible with Docker Desktop's Compose runtime
- Not necessarily compatible with older `docker-compose` v1 (deprecated)

### AppHost → Compose Tracking

When the AppHost changes, regenerate `compose.yaml` to stay in sync:

```bash
# After modifying src/StreamingDigest.AppHost/AppHost.cs
./scripts/publish_compose.sh

# Review the diff
git diff compose.yaml

# Commit both AppHost changes and the updated artifact
git add src/StreamingDigest.AppHost/ compose.yaml
git commit -m "chore: Update AppHost and regenerate compose.yaml"
```

**Why track `compose.yaml` in git?**
- Provides a fixed artifact for CI/CD and deployment pipelines
- Allows reviewers to see infrastructure changes in PRs
- Enables rollback if regeneration produces unexpected changes
- Serves as a checkpoint: compose.yaml reflects a known-good AppHost state

## Deploying with Compose

### Starting a Fresh Stack

```bash
# 1. Clone the repository
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest

# 2. Set up environment
cp .env.example .env
# Edit .env with your secrets and paths

# 3. Start the stack
docker compose up -d

# 4. Verify all services are running
docker compose ps

# 5. Check logs for errors
docker compose logs --tail=50 streaming-digest-api
```

### Restarting After Changes

```bash
# If AppHost or compose.yaml changed upstream:
git pull

# Regenerate compose.yaml locally (in case it changed)
./scripts/publish_compose.sh

# Restart services
docker compose down
docker compose up -d

# Verify
docker compose ps
docker compose logs -f streaming-digest-api
```

### Database Persistence Across Restarts

Postgres data is stored in the `streamingdigest-postgres18-data` volume. This persists across container restarts.

**If you want to reset the database:**

```bash
# 1. Stop the stack
docker compose down

# 2. Remove the Postgres volume (this DELETES all data)
docker volume rm streaming-digest_streamingdigest-postgres18-data

# 3. Restart (Postgres will reinitialize)
docker compose up -d
```

**Warning**: Removing the volume is irreversible. Make backups first if needed.

### Updating to a New AppHost Release

When upstream publishes a new `compose.yaml`:

```bash
# 1. Update your local repo
git pull

# 2. If compose.yaml changed, verify the changes
git diff HEAD~1 compose.yaml

# 3. Optionally regenerate locally (to get latest Aspire CLI behavior)
./scripts/publish_compose.sh

# 4. Restart the stack
docker compose down
docker compose up -d
```

**If the database schema changed**: The API migrations run automatically on startup. Check logs to verify:
```bash
docker compose logs streaming-digest-api | grep -i migration
```

## Impact on Running Deployments

### When You Regenerate

Regenerating `compose.yaml` does NOT automatically restart services. You must explicitly restart:

```bash
docker compose down
docker compose up -d
```

**Before restarting**, consider:
- **Read-only data loss**: Postgres loses uncommitted transactions
- **In-flight requests**: API connections drop
- **Scheduled jobs**: Hangfire jobs in progress are interrupted

**Graceful restart:**
```bash
# 1. Pause Hangfire job processing
# (No SDK method; must use API or database directly)

# 2. Allow in-flight requests to complete (~30 seconds)
sleep 30

# 3. Stop and restart
docker compose down
docker compose up -d

# 4. Verify API is healthy
curl http://localhost:8000/api/health
```

### Zero-Downtime Regeneration (Future)

For production deployments requiring zero downtime:
- Use Kubernetes rolling updates (beyond this guide; see "Advanced Scenarios")
- Use a reverse proxy to drain connections before shutdown
- Use Aspire's deployment targets (Azure Container Apps, AKS) which handle orchestration

### Rollback

If regeneration breaks the stack:

```bash
# 1. Revert the compose.yaml
git checkout compose.yaml

# 2. Restart
docker compose down
docker compose up -d

# 3. Investigate the issue
# (File a bug; run the new AppHost locally to reproduce)
```

## Advanced Scenarios

### Kubernetes Deployment

For Kubernetes, use Aspire's publish target:

```bash
aspire publish \
  --apphost src/StreamingDigest.AppHost \
  --output-format kubernetes \
  --output-path k8s/
```

This generates Kubernetes manifests (Deployments, Services, ConfigMaps) instead of Compose.

See the [Aspire Kubernetes Publishing docs](https://learn.microsoft.com/en-us/dotnet/aspire/deployment/overview) for details.

### Multi-Machine Deployment

For production spanning multiple machines:

1. **Compose is single-machine** (all services on one Docker daemon)
2. **Use Kubernetes or orchestration platforms** for multi-machine
3. **Or split Compose** (e.g., database on one machine, API on another) with manual wiring

Streaming Digest is optimized for Compose (single machine) or Kubernetes. Multi-machine Compose requires manual coordination.

### Custom Resource Definitions

To add a custom service to the AppHost (e.g., a caching layer):

1. **Modify `src/StreamingDigest.AppHost/AppHost.cs`**:
   ```csharp
   var redis = builder.AddRedis("redis")
       .WithImage("redis", "7.2");
   
   api.WithReference(redis);
   ```

2. **Regenerate**:
   ```bash
   ./scripts/publish_compose.sh
   ```

3. **Review and commit**:
   ```bash
   git diff compose.yaml
   git add AppHost.cs compose.yaml
   git commit -m "feat: Add Redis caching layer"
   ```

## Troubleshooting

### "Aspire CLI not found"

```
Aspire CLI not found. Install it or add it to PATH, or place it at
~/.aspire/bin/aspire or ~/.dotnet/tools/aspire.
```

**Solution**:
```bash
dotnet tool install --global Aspire.Cli
aspire --version
```

### "docker-compose.yaml was not generated"

Verify the AppHost compiles:
```bash
dotnet build src/StreamingDigest.AppHost
```

Then try publish with verbose output:
```bash
aspire publish \
  --apphost src/StreamingDigest.AppHost \
  --output-path /tmp/aspire-out \
  --non-interactive
ls -la /tmp/aspire-out/
```

### Compose fails to start: "Environment variable not set"

```
Error: required variable not set
```

Ensure `.env` exists and has all required variables:
```bash
cp .env.example .env
# Edit .env with your values
docker compose config  # Validates and shows the resolved config
```

### Postgres container crashes after regeneration

**Likely cause**: Changed database credentials in `.env` without resetting the volume.

**Solution**:
```bash
# Postgres persists the old credentials in the volume
# You must either:
# 1. Update .env to match the persisted credentials, OR
# 2. Delete the volume and reinitialize
docker volume rm streaming-digest_streamingdigest-postgres18-data
docker compose down && docker compose up -d
```

## Summary

| Task | Command |
|------|---------|
| Regenerate compose.yaml | `./scripts/publish_compose.sh` |
| Start the stack | `docker compose up -d` |
| View logs | `docker compose logs -f <service>` |
| Stop the stack | `docker compose down` |
| Reset database | `docker volume rm streaming-digest_*-data` |
| Verify health | `curl http://localhost:8000/api/health` |

## Related Documentation

- [Aspire AppHost Configuration](../src/StreamingDigest.AppHost/AppHost.cs)
- [Local Admin Health Contract](./LIVE_ADMIN_HEALTH_CONTRACT.md)
- [Aspire Hosting Official Docs](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Docker Compose Specification](https://github.com/compose-spec/compose-spec)
