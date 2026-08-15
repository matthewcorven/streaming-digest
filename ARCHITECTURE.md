# Architecture

This document describes the system architecture of streaming-digest, including service topology, deployment patterns, and conventions.

## Docker Image Naming

All Aspire-managed container images follow a standardized naming convention to ensure consistent identification, traceability, and deployment across environments.

### Naming Pattern

```
streaming-digest-{service-name}:{short-commit-id}
```

**Example:** `streaming-digest-api:a20b441`

### Services with Image Tags

The following services automatically receive image tags based on the current git commit ID:

| Service | Image Name | Source |
|---------|------------|--------|
| API (.NET) | `streaming-digest-api:{commit-id}` | `AddProject<StreamingDigest.Api>()` |
| Worker (.NET) | `streaming-digest-worker:{commit-id}` | `AddProject<StreamingDigest.Worker>()` |
| Scraper (Node.js) | `streaming-digest-scraper:{commit-id}` | `AddDockerfile("scraper", ...)` |
| Whisper (Audio-to-Text) | `streaming-digest-whisper:{commit-id}` | `AddContainer("whisper", ...)` |

### How It Works

The `AppHost.cs` orchestrator:
1. Runs `git rev-parse --short HEAD` to obtain the current commit ID (e.g., `a20b441`)
2. Injects the commit ID as the image tag for all Aspire-managed services
3. Falls back to `latest` tag if git is unavailable or the repository is not a git checkout

This ensures that:
- **Every build is traceable** to the exact commit that built it
- **Images are immutable** by commit ID, preventing accidental overwrites
- **Local and CI builds share the same image naming** for consistency
- **Deployment artifacts are easily auditable** in Docker registries

### Implementation Details

**File:** `src/StreamingDigest.AppHost/AppHost.cs`

**Key Constants:**
- `const string imageNamePrefix = "streaming-digest"`
- `var shortCommitId = GetShortCommitId()` — runs `git rev-parse --short HEAD`

**Service Configurations:**
```csharp
var api = builder.AddProject<Projects.StreamingDigest_Api>("api")
    .WithImage($"{imageNamePrefix}-api")
    .WithImageTag(shortCommitId)
    // ...

builder.AddProject<Projects.StreamingDigest_Worker>("worker")
    .WithImage($"{imageNamePrefix}-worker")
    .WithImageTag(shortCommitId)
    // ...

var scraper = builder.AddDockerfile("scraper", "../StreamingDigest.Scraper")
    .WithImageTag(shortCommitId)
    // ...

var whisper = builder.AddContainer("whisper", "streaming-digest-whisper")
    .WithImageTag(shortCommitId)
    // ...
```

## Service Topology

- **API** (.NET 10 + Aspire): Primary REST/gRPC service for ingestion, query, and administration
- **Worker** (.NET 10 + Aspire): Background job processing (video transcoding, embedding generation)
- **Scraper** (Node.js + Aspire): HTTP service for feed parsing, snapshot capture, and metadata extraction
- **Whisper** (MLX Whisper): Optional audio-to-text runtime for caption generation (Apple Silicon optimized)
- **PostgreSQL + pgvector** (Aspire container): Relational data + embedding storage
- **Ollama** (Aspire container): LLM and embedding model runtime
- **Observability Stack** (Aspire containers): Prometheus, Loki, Tempo, Grafana, OTel Collector

## Deployment Modes

- **Local Development:** `aspire start` orchestrates all services in Docker, with live reload via hot reload
- **Docker Compose:** `aspire publish` generates a fully standalone Docker Compose manifest
- **Kubernetes/Cloud:** Future deployments via `aspire publish` with cloud-specific generators

## Image Naming Examples

**Current Commit `a20b441`:**
```
streaming-digest-api:a20b441
streaming-digest-worker:a20b441
streaming-digest-scraper:a20b441
streaming-digest-whisper:a20b441
```

**After a New Commit (e.g., `c7f2e19`):**
```
streaming-digest-api:c7f2e19
streaming-digest-worker:c7f2e19
streaming-digest-scraper:c7f2e19
streaming-digest-whisper:c7f2e19
```

All images generated in a single `aspire publish` share the same commit ID tag, ensuring atomic consistency.
