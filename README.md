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
  - link classification
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
- traces are stored in Tempo
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

Streaming Digest uses local OSS AI services.

Embeddings:

- Microsoft Semantic Kernel talks to Ollama
- `bge-m3` is preferred when available
- `nomic-embed-text` is documented as a simpler alternative

Local LLM:

- configurable Ollama model
- recommended class: small local instruction model, such as Llama 3.1/3.2 8B-class or suitable Phi-class model depending on hardware
- used for semantic segmentation and link classification

Audio-to-text:

- local CPU/GPU-capable engine
- designed behind a Semantic Kernel-style abstraction
- whisper.cpp is the preferred practical backend when compatible

## Security

Streaming Digest is intended for private on-prem use, typically accessed through Tailscale.

Security features:

- single-user login
- bootstrap admin credentials from environment variables on first startup
- password hashed with Argon2id and stored in PostgreSQL
- forced password change after bootstrap
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

Prerequisites:

- Docker and Docker Compose
- Tailscale or another private network access method
- sufficient disk space for screenshots, transcripts, embeddings, and telemetry
- local model files pulled into Ollama
- Matrix bot account and room ID prepared; encrypted room readiness is MVP+

Start the stack:

```bash
docker compose -p streaming-digest up -d
```

Open the web application:

```text
http://localhost:8080
```

On first startup:

1. Sign in using the bootstrap admin credentials configured in the environment.
2. Change the bootstrap password.
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

Detailed project documents are available in `docs/`:

- `docs/product/PRD.md`
- `docs/architecture/ARCHITECTURE.md`
- `docs/architecture/DATA_MODEL.md`
- `docs/api/API_SPEC.md`
- `docs/implementation/IMPLEMENTATION_PLAN.md`
- `docs/operations/UPGRADE_PATHS.md`

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

Streaming Digest has reached MVP completeness for the agreed hard-MVP scope:

- ingestion
- local transcript fallback
- semantic segmentation
- screenshots
- link/repository/website processing
- hybrid search
- curation and notes
- Matrix notifications; E2EE is MVP+
- observability
- on-prem Compose deployment

Future work can expand the product with YouTube OAuth subscription import, Shorts support, multi-user collaboration, recursive website crawling, repository source-code indexing, public sharing/export workflows, and mobile-native clients.
