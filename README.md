# Streaming Digest

Streaming Digest is a self-hosted personal YouTube knowledge ingestion, search, and curation application.

It monitors your subscribed YouTube channels, ingests new long-form videos, extracts transcripts, semantic segments, screenshots, links, repository metadata, website content, and notes, then makes everything searchable through a hybrid text + vector search interface.

The application is designed for on-prem deployment with Docker Compose, PostgreSQL + pgvector, local OSS AI models, Matrix end-to-end encrypted notifications, and production-grade observability.

## What Streaming Digest does

Streaming Digest helps answer questions like:

> “Which video mentioned a code project that searches for project ideas not yet achieved across all of GitHub?”

Instead of searching YouTube manually, Streaming Digest searches across:

- video titles and descriptions
- transcript chunks
- author-provided chapters
- semantic video segments
- timecoded screenshots
- external links
- scraped website metadata and page text
- GitHub repository metadata
- GitLab and Bitbucket repository metadata when MVP+ support is enabled
- repository README files
- private personal notes

Search results are ranked using hybrid keyword + semantic vector search and link directly back to the relevant YouTube timestamp, repository, website, author channel, or note.

## Features

### YouTube ingestion

- Manual channel list management
- Scheduled and manual ingestion runs
- Configurable max-age lookback, defaulting to 30 days
- User-triggered backfill with independent days and max-video limits
- Regular public long-form videos only
- yt-dlp/OSS-first ingestion
- Optional YouTube Data API key support
- Video metadata extraction:
  - author/channel
  - title
  - description
  - publish date
  - duration
  - channel profile URL
  - video URL
- Existing YouTube captions/transcripts when available
- Automatic local audio-to-text fallback for videos without captions
- Temporary audio/video files deleted after processing

### Segments and screenshots

- Author-provided chapters/timecodes when available
- Semantic transcript segmentation when chapters are unavailable
- Local LLM-assisted segment title and boundary refinement
- Configurable segment cap, defaulting to 60 segments per video
- One WebP screenshot per segment
- Configurable screenshot offset, defaulting to segment start + 5 seconds
- Screenshot files stored on a mounted volume with metadata in PostgreSQL

### Link and repository ingestion

- Link extraction from video descriptions
- Best-effort pinned-comment link extraction
- URL normalization and tracking parameter removal
- Link classification using rules, local LLM classification, and user correction history
- All links are retained, including likely ads/sponsors, with classification metadata
- Repository support for GitHub in MVP
- GitLab and Bitbucket repository support are MVP+
- Repository metadata ingestion
- README ingestion and embedding
- LICENSE ingestion
- DeepWiki URL detection when a real DeepWiki page exists

### Website scraping

- Crawlee/Playwright-based first-page scraping
- robots.txt and per-host rate-limit aware
- Extracts:
  - final URL
  - page title
  - description
  - OpenGraph/Twitter metadata
  - visible page text
- Page text is stored and embedded
- Raw HTML is not stored by default
- Optional per-video raw HTML debug capture is available

### Search

- PostgreSQL full-text search
- Trigram/partial matching
- pgvector semantic search
- Hybrid text + vector ranking with configurable weights
- Unified ranked results across videos, segments, links, repositories, scraped pages, and notes
- Result explanations showing why each item matched
- Filters for:
  - channel
  - date range
  - result type
  - transcript availability
  - repository availability
  - notes availability
  - ingestion status

### Curation

- Edit modal for correcting or overriding scraped values
- Original scraped values are preserved
- Override history records previous values and change timestamps
- Re-generated embeddings use user overrides
- Editable fields include:
  - video title
  - video description
  - segment title
  - segment summary/description
  - transcript text
  - external link title
  - external link description
  - external link classification
  - repository metadata
- Private markdown notes using EasyMDE
- Notes attach to videos, segments, external links, and repositories
- Notes are embedded and searchable

### Notifications

- Matrix notifications in MVP
- Matrix end-to-end encryption is MVP+
- Dedicated Matrix bot account
- Separate Matrix notifier service/container
- MVP unencrypted Matrix room ID configuration
- Manual Android client/device verification and encrypted Matrix room readiness are MVP+
- Notifications for manual and scheduled runs by default
- Summary includes:
  - channels checked
  - new videos found
  - videos ingested
  - videos failed or skipped
  - transcripts found or missing
  - repositories found
  - dashboard link to the ingestion run
high-signal matches similar to recent searches
  - active rate-limit deferments
  - 
### Observability

Streaming Digest is observable locally and in production.

Local development:

- Aspire dashboard

Production deployment:

- OpenTelemetry Collector
- Prometheus
- Grafana
- Loki
- Tempo

The web application includes operational links to dashboards such as Grafana and Hangfire.

Telemetry policy:

- domain events and warning/error summaries are stored in PostgreSQL
- full logs are stored in Loki
- metrics are stored in Prometheus
- telemetry retention follows the first-run disk policy: 90 days when free space is above 5 GB, 30 days above 1 GB, disabled with a warning otherwise
- logs, metrics, and traces retain 90 days by default

### Admin operations

The admin UI supports:

- add, remove, pause, and resume channels
- run ingestion now
- run channel backfill
- retry failed video ingestion
- retry failed link or repository ingestion
- regenerate embeddings for one item
- regenerate all embeddings after model change
- purge screenshots for a video or channel
- test Matrix notification
- test embedding service
- test audio-to-text service
- open Hangfire dashboard
- open Grafana and observability dashboards

## Architecture

Streaming Digest runs as a Docker Compose application with related containers sharing the `streaming-digest-*` naming convention.

Core services:

- `streaming-digest-api` — ASP.NET Core 10 API and hosted Blazor WASM application
- `streaming-digest-worker` — Hangfire ingestion and processing worker
- `streaming-digest-postgres` — PostgreSQL + pgvector data store
- `streaming-digest-ollama` — local embeddings and local LLM runtime
- `streaming-digest-whisper` — local audio-to-text service
- `streaming-digest-scraper` — Crawlee/Playwright scraper service
- `streaming-digest-matrix-notifier` — Matrix notification service; E2EE is MVP+
- `streaming-digest-otel-collector` — OpenTelemetry Collector
- `streaming-digest-prometheus` — metrics store
- `streaming-digest-grafana` — dashboards
- `streaming-digest-loki` — log store
- `streaming-digest-tempo` — trace store

Default ports:

- API + Blazor: `8080`
- Grafana: `3000`
- Prometheus: `9090`
- Loki: `3100`
- Tempo: `3200`
- OpenTelemetry Collector: `4317` and `4318`
- Ollama: `11434`
- Hangfire dashboard: `/admin/jobs` under the API

PostgreSQL is internal by default.

## Technology stack

- ASP.NET Core 10
- Blazor WebAssembly
- Aspire
- Docker Compose
- PostgreSQL
- pgvector
- EF Core + Npgsql
- Dapper for tuned search queries
- Hangfire with PostgreSQL storage
- Microsoft Semantic Kernel
- Ollama
- local audio-to-text, typically whisper.cpp-backed
- yt-dlp
- Crawlee + Playwright
- Matrix notifier service; E2EE is MVP+
- OpenTelemetry
- Prometheus
- Grafana
- Loki
- Tempo

## Local AI model support

Streaming Digest uses local OSS AI services managed through a hard-coded model catalog. Models can be downloaded and verified from the Settings → Models tab or via the API.

**Embedding models:**

| ID | Provider | Downloadable |
|---|---|---|
| `bge-m3` | Ollama | ✓ (`ollama pull bge-m3`) |
| `text-embedding-3-small` | OpenAI | verify-only (external) |

`bge-m3` is the default local embedding model. `text-embedding-3-small` is in the catalog for deployments using the OpenAI API; no local download is required.

**LLM models:**

| ID | Provider | Downloadable |
|---|---|---|
| `llama3.1:8b` | Ollama | ✓ (`ollama pull llama3.1:8b`) |
| `qwen2.5:7b` | Ollama | ✓ (`ollama pull qwen2.5:7b`) |

Used for semantic segmentation and link classification. Both are small local instruction models suitable for CPU/GPU hardware.

**Audio-to-text:**

| ID | Provider | Downloadable |
|---|---|---|
| `whisper` | Whisper | verify-only (runtime managed externally) |

The Whisper runtime is managed outside Ollama. The API probes the audio-to-text service `/health` endpoint to verify presence.

**Model lifecycle API:**

- `GET /api/models/options` — catalog with download commands and mount hints.
- `POST /api/models/download` — queues an Ollama model pull (202 Accepted + `operationId`).
- `POST /api/models/verify` — real presence probe; writes `model_runtime_state`.
- `GET /api/models/status` — cross-process authoritative runtime state for all models.
- `GET /api/models/events` — SSE stream for live download progress (event types: `model.status`, `operation.status`, `operation.completed`, `operation.failed`).

## Security

Streaming Digest is intended for private on-prem use, typically accessed through Tailscale.

Security features:

- single-user login
- first-run setup UI when no app user exists
- optional bootstrap admin credentials from environment variables on first startup
- password hashed with Argon2id and stored in PostgreSQL
- forced password change only for environment-bootstrapped credentials
- secure HTTP-only cookies
- CSRF protection for mutating endpoints
- login rate limiting
- internal-only service networking by default
- separate Matrix bot account and persisted bot session/config store; E2EE crypto/session store is MVP+

## Backup and restore

Back up these components:

- PostgreSQL database
- screenshot/media volume
- optional raw HTML debug-capture volume
- Matrix bot session/config store; E2EE crypto/session store is MVP+
- configuration and secrets
- observability data if long-term telemetry history matters

A restored system should verify:

- login works
- search works
- screenshots load
- Matrix encrypted test notification sends
- embedding service test succeeds
- audio-to-text service test succeeds

## Quick start

Streaming Digest is designed for **zero-intervention onboarding** — fresh `docker compose up -d` starts all services cleanly without manual intervention.

**Quick links by audience:**

- **👤 End users deploying Streaming Digest?** → **[User Onboarding Guide](./docs/operations/ONBOARDING_USERS.md)** for deployment, first-run setup, and operations
- **👨‍💻 Developers and OSS contributors?** → **[Developer Onboarding Guide](./docs/operations/ONBOARDING_DEVELOPERS.md)** for development setup, architecture, and contributing
- **📖 Full feature overview?** → **[Onboarding Feature Doc](./ONBOARDING.md)** for technical details and how it works

### Deploy in 3 commands

Prerequisites:

- Docker and Docker Compose
- Tailscale or another private network access method
- sufficient disk space for screenshots, transcripts, embeddings, and telemetry

Create a local environment file from the deployment template and then start the stack:

```bash
cp .env.example .env
docker compose up -d
```

All critical services reach healthy state within ~60 seconds. Then open http://localhost:8080 and create your user account.

`compose.yaml` is a checked-in artifact generated from the Aspire AppHost rather than the source of truth.

Regenerate it so the committed Compose deployment stays aligned with the current AppHost resource graph and service wiring, including observability resources such as Grafana.

Run the publish script whenever you change AppHost deployment behavior, such as service/resource wiring, exposed endpoints, environment propagation, or other changes that affect Compose output. Do not hand-edit `compose.yaml`; republish it before committing deployment-shape changes.

From the repository root, run:

```bash
./scripts/publish_compose.sh
```

The script runs `aspire publish` for `src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj`, then replaces the repository-root `compose.yaml` with the generated `docker-compose.yaml` artifact.

Open the web application:

```text
http://localhost:8080
```

On first startup:

1. If no bootstrap admin user was created from environment variables, open `/setup` and create the first account in the web UI.
2. If bootstrap admin credentials were configured in the environment, sign in with them and complete the forced password change.
3. Configure Ollama embedding and LLM models.
4. Configure the local audio-to-text service.
5. Configure Matrix bot credentials and room ID; encrypted room setup is MVP+.
6. Send a Matrix test notification.
7. Add YouTube channels.
8. Run manual ingestion or configure the schedule.

## Common workflows

### Add a channel

1. Open Channels.
2. Add a YouTube channel URL or channel ID.
3. Confirm resolved channel metadata.
4. Optionally set per-channel ingestion settings.
5. Run ingestion now or wait for the schedule.

### Search ingested knowledge

1. Open Search.
2. Enter a natural-language query or partial keyword query.
3. Adjust filters if needed.
4. Review ranked results and explanations.
5. Open the YouTube timestamp, repository, website, or note.

### Correct metadata

1. Open a result card.
2. Choose Edit.
3. Update the field override.
4. Save.
5. Streaming Digest records history and regenerates affected embeddings.

### Add a note

1. Open a video, segment, repository, or link result.
2. Choose Notes.
3. Write markdown using the EasyMDE editor.
4. Save.
5. The note becomes searchable.

### Review ingestion status

1. Open Ingestion Runs.
2. Select a run.
3. Review videos processed, failures, skipped items, transcript status, and repository counts.
4. Retry failed items if needed.

## Documentation

### Getting started

**New to Streaming Digest?** Start here:

- **[ONBOARDING.md](./ONBOARDING.md)** — feature overview and architecture of zero-intervention onboarding
- **[User Onboarding Guide](./docs/operations/ONBOARDING_USERS.md)** — deployment, first-run setup, operations, troubleshooting
- **[Developer Onboarding Guide](./docs/operations/ONBOARDING_DEVELOPERS.md)** — development setup, contributing, project structure

### Detailed project documents

Available in `docs/`:

- `docs/product/PRD.md` — product requirements and roadmap
- `docs/architecture/ARCHITECTURE.md` — system design and component responsibilities
- `docs/architecture/DATA_MODEL.md` — entity relationships and database schema
- `docs/api/API_SPEC.md` — REST API endpoints and schemas
- `docs/presentation/PRESENTATION.md` — UI/UX design and user workflows
- `docs/adr/` — architectural decision records
- Implementation work is tracked as GitHub issues (migrated from the retired implementation plan)
- `docs/operations/UPGRADE_PATHS.md`

## Development

**Getting started with development?** → **[Developer Onboarding Guide](./docs/operations/ONBOARDING_DEVELOPERS.md)** for setup, project structure, and workflow.

### How the project is organized

- **Issue-driven tracking** — implementation work lives in GitHub issues labeled `slice-*` (build order, prototypes first), `phase-*` (requirement grouping), and `squad:{member}` (owning agent).
- **Squad AI team** — this repo is developed by a Squad AI team (`.squad/`): roster and routing in `.squad/team.md` / `.squad/routing.md`, agent charters and histories in `.squad/agents/`, and team decisions indexed in `.squad/decisions.md`. Architectural decisions get full ADRs in `docs/adr/`; team/process/scope decisions live in the decisions index.
- **Verification evidence** — durable verification results (benchmarks, recall reports, cross-platform checks, restore dry-runs, prototype comparisons) are committed append-only under `docs/verification/` as `{task-id}-{slug}.md`, with machine-readable JSON alongside for numeric results. Quality gates citing measured targets are not met until the evidence artifact is committed.

### Local development

Start the Aspire stack:

```bash
dotnet run --project src/StreamingDigest.AppHost
```

Reset local containers, volumes, and repo-local processes to a clean first-run state:

```bash
./scripts/reset_local_state.sh
```

This starts all services and opens the Aspire dashboard at http://localhost:18888 automatically.

Run tests:

```bash
dotnet test
```

See [Developer Onboarding Guide](./docs/operations/ONBOARDING_DEVELOPERS.md) for debugging, database migrations, and updating the Docker Compose setup.

### .NET User Secrets

Use .NET User Secrets for local development credentials instead of committing secrets to `appsettings.json`, `.env`, or source files. The AppHost project already has a `UserSecretsId`, so you can manage its local secrets directly with the .NET CLI.

Useful commands:

```bash
dotnet user-secrets list --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets set "Parameters:postgres-username" "streamingdigest" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets set "Parameters:postgres-password" "replace-me" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets set "Parameters:grafana-admin-user" "admin" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets set "Parameters:grafana-admin-password" "replace-me" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets set "Parameters:pgadmin-default-email" "admin@streamingdigest.dev" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets set "Parameters:pgadmin-default-password" "replace-me" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
```

To remove a single value or clear the local secret store for the AppHost:

```bash
dotnet user-secrets remove "Parameters:pgadmin-default-password" --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
dotnet user-secrets clear --project src/StreamingDigest.AppHost/StreamingDigest.AppHost.csproj
```

The AppHost reads these values from the `Parameters:*` configuration keys and passes them through to local resources such as PostgreSQL, Grafana, and pgAdmin at startup.

Official guidance: [Safe storage of app secrets in development in ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)

## Legal and privacy notes

Streaming Digest is designed for personal archival and search use on infrastructure you control.

You are responsible for complying with:

- YouTube Terms of Service
- copyright rules for transcripts, screenshots, and video-derived artifacts
- robots.txt and website scraping expectations
- repository licenses and attribution requirements
- Matrix credential protection; E2EE session protection applies when MVP+ E2EE is enabled

Do not expose the application publicly without reviewing authentication, transport security, rate limits, and secret handling.

## Project status
is pre-implementation: product, architecture, data model, API, presentation, and operations docs are agreed, and the hard-MVP scope is decomposed into GitHub issues ready to build. Progress and current state are visible on the issue tracker.

Future work beyond MVP
Future work can expand the product with YouTube OAuth subscription import, Shorts support, multi-user collaboration, recursive website crawling, repository source-code indexing, public sharing/export workflows, and mobile-native clients.
