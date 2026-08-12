# Zero-Intervention Onboarding

Streaming Digest is designed to start cleanly from a fresh clone or deployment without manual intervention. This document describes the onboarding feature and points you to the right guide for your use case.

## What is zero-intervention onboarding?

Zero-intervention onboarding means:

✅ **Fresh `docker compose up -d`** starts all services without human intervention  
✅ **All critical services** (API, Whisper, Scraper, PostgreSQL, Ollama) reach healthy state automatically  
✅ **Graceful degradation** — optional services (Matrix notifications, external models) don't block startup  
✅ **Safe defaults** — `.env.example` contains production-ready local-only settings  
✅ **Service dependencies** — Docker Compose enforces proper startup ordering via health checks  
✅ **No manual post-startup fixes** — healthy state persists without administration  

## Quick start

**For end-users deploying Streaming Digest:**

```bash
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest
cp .env.example .env
docker compose up -d
```

Open http://localhost:8080 and complete the first-run setup.

👉 **Read [User Onboarding Guide](./docs/operations/ONBOARDING_USERS.md)** for detailed deployment and first-run steps.

## For developers and OSS contributors

**Setting up a development environment:**

```bash
git clone https://github.com/matthewcorven/streaming-digest.git
cd streaming-digest
dotnet restore
dotnet build
dotnet run --project src/StreamingDigest.AppHost
```

The Aspire dashboard opens automatically at http://localhost:18888.

👉 **Read [Developer Onboarding Guide](./docs/operations/ONBOARDING_DEVELOPERS.md)** for development setup, contributing workflow, and architectural context.

## How it works

### Zero-intervention is a built-in feature

Streaming Digest achieves zero-intervention onboarding through:

1. **MSBuild publish targets** — removes duplicate transitive app settings files during Docker image builds, preventing NETSDK1152 build failures
2. **Docker healthchecks** — all service images include curl and expose `/health` endpoints so Compose can verify readiness
3. **Compose service dependencies** — `depends_on` with `condition: service_healthy` enforces startup ordering (API waits for Whisper, etc.)
4. **Graceful degradation** — optional services like Matrix notifications and external AI models are safe to disable
5. **Safe environment defaults** — `.env.example` ships with local-only, single-user defaults that "just work"

### What's NOT needed on first startup

- Database schema creation — the API automatically provisions on first run
- Model downloads — models are optional; the system operates in degraded mode until configured
- Matrix bot account — notifications default to disabled
- External API keys — YouTube yt-dlp and local Whisper work without configuration

### What you need to do

**First time:**
1. Clone the repo and run `docker compose up -d`
2. Open http://localhost:8080 and create your user account
3. (Optional) Configure local AI models via Settings → Models
4. (Optional) Add YouTube channels and run ingestion

**Ongoing:**
- Run scheduled ingestion or trigger manual runs from the admin panel
- Search and curate your knowledge base
- Backup PostgreSQL and screenshots periodically

## Architecture notes

See [Architecture](./docs/architecture/ARCHITECTURE.md) and [Data Model](./docs/architecture/DATA_MODEL.md) for technical depth.

The onboarding feature is implemented via:

- **`.env.example`** — safe defaults for local deployment
- **`compose.yaml`** — auto-generated from Aspire, includes healthchecks and dependencies
- **`src/StreamingDigest.Api/Dockerfile`** — includes curl for healthchecks
- **`Dockerfile.whisper`** — includes curl for healthchecks
- **`src/StreamingDigest.*.csproj`** — MSBuild targets to prevent publish conflicts
- **Service implementations** — graceful degradation when optional services unavailable

## Troubleshooting

### Services not reaching healthy state

Check service logs:

```bash
docker compose logs streaming-digest-api
docker compose logs streaming-digest-whisper
docker compose logs streaming-digest-postgres
```

Common issues:
- **Port conflicts** — another service already using port 8080 or 5432
- **Disk space** — insufficient space for PostgreSQL or Ollama models
- **Memory** — Ollama needs 4+ GB available for model inference

### Rebuild images after code changes

```bash
docker compose down
docker compose up -d --build
```

### Reset to a clean state

```bash
docker compose down -v  # -v removes volumes including database
docker compose up -d
```

## Contributing

When adding new features:

1. **Update the compose.yaml** — run `./scripts/publish_compose.sh` after Aspire AppHost changes
2. **Add healthchecks** — all new services should expose `/health` and define Docker healthcheck
3. **Add graceful degradation** — optional services must not block startup
4. **Update `.env.example`** — add any new environment variables with safe defaults
5. **Test fresh startup** — `docker compose down -v && docker compose up -d` and verify all services healthy

## Documentation

- **[User Onboarding Guide](./docs/operations/ONBOARDING_USERS.md)** — deploying and operating Streaming Digest
- **[Developer Onboarding Guide](./docs/operations/ONBOARDING_DEVELOPERS.md)** — setting up development, contributing, running tests
- **[Architecture](./docs/architecture/ARCHITECTURE.md)** — technical architecture and design decisions
- **[Product PRD](./docs/product/PRD.md)** — feature requirements and roadmap
- **[ADRs](./docs/adr/)** — architectural decision records

---

**Last updated:** 2026-08-11  
**Status:** Zero-intervention onboarding verified ✅
