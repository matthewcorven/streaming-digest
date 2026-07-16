# Streaming Digest Technical Architecture

Status: MVP architecture agreed
Target runtime: ASP.NET Core 10, Blazor WASM, Aspire, Docker Compose, PostgreSQL + pgvector

## 1. Architecture goals

Streaming Digest must be:

- Self-hosted on an on-prem Linux Docker host.
- Started with a single Docker Compose command generated or maintained from Aspire deployment artifacts.
- Observable locally through Aspire and in deployment through Prometheus/Grafana/Loki/Tempo/OpenTelemetry Collector.
- Private by default and suitable for Tailscale-only access.
- Single-user authenticated.
- Capable of local embeddings, local LLM inference, and local audio-to-text.
- Robust against partial ingestion failures.
- Designed for hybrid text/vector search over YouTube-derived knowledge artifacts.

## 2. Logical services

### 2.1 API and Blazor host: `streaming-digest-api`

Responsibilities:

- Hosts ASP.NET Core 10 REST API.
- Hosts Blazor WASM static assets.
- Handles login/session/cookie auth.
- Serves search, admin, notes, edit, and ingestion status endpoints.
- Hosts Hangfire dashboard at `/admin/jobs` behind authentication.
- Exposes health checks and OpenTelemetry instrumentation.

### 2.2 Worker: `streaming-digest-worker`

Responsibilities:

- Runs Hangfire jobs.
- Performs channel/video ingestion.
- Calls yt-dlp/YouTube adapters.
- Calls transcript/audio-to-text services.
- Calls segmentation/classification services.
- Calls browser scraper.
- Calls repository adapters.
- Generates screenshots.
- Writes ingestion events and search documents.
- Queues Matrix notifications after manual/scheduled runs.

### 2.3 PostgreSQL: `streaming-digest-postgres`

Responsibilities:

- Primary relational data store.
- pgvector vector store.
- Full-text search indexes.
- Hangfire storage.
- Domain events and warning/error summaries.

Required extensions:

- `vector` for pgvector.
- `pg_trgm` for partial/fuzzy text matching.
- `unaccent` if needed for text normalization.

### 2.4 Ollama: `streaming-digest-ollama`

Responsibilities:

- Local embedding model endpoint.
- Local LLM endpoint.

Accessed via Microsoft Semantic Kernel adapters.

Default recommendation:

- Embeddings: prefer `bge-m3` if available, otherwise document `nomic-embed-text`.
- LLM: configurable small local instruction model with sufficient context and JSON reliability.

### 2.5 Audio-to-text service: `streaming-digest-whisper`

Responsibilities:

- Provides local speech-to-text for videos without captions.
- Runs CPU or GPU depending host capabilities.
- Preferred implementation: whisper.cpp if compatible with Semantic Kernel-style `AudioToTextClientBase` adapter and target platforms.

The service should expose an internal HTTP or gRPC API:

- Input: temporary audio file path or uploaded audio stream.
- Output: transcript with timestamps.
- Metadata: model name, language, duration, confidence if available.

### 2.6 Browser scraper: `streaming-digest-scraper`

Responsibilities:

- Crawlee/Playwright-based first-page scraping.
- Extract visible text and metadata.
- Respect robots.txt.
- Enforce per-host rate limits.
- Optional per-video raw HTML debug capture.

This may be integrated into the worker container or kept separate. Prefer separate container if browser dependencies make the worker image too heavy.

### 2.7 Matrix notifier: `streaming-digest-matrix-notifier`

Responsibilities:

- Dedicated Matrix E2EE notification service.
- Maintains Matrix device/session/crypto store on durable volume.
- Sends encrypted notifications to configured room ID.
- Supports one-time login and manual verification via Android Matrix client.

Keep this separate from the API/worker so Matrix crypto dependencies and state are isolated.

### 2.8 Observability stack

Containers:

- `streaming-digest-otel-collector`
- `streaming-digest-prometheus`
- `streaming-digest-grafana`
- `streaming-digest-loki`
- `streaming-digest-tempo`

Responsibilities:

- OTel Collector receives OTLP traces/metrics/logs.
- Prometheus stores metrics.
- Loki stores full logs.
- Tempo stores traces.
- Grafana visualizes metrics/logs/traces and may query Postgres for domain dashboards.

## 3. Container naming and ports

Use Compose project/base name: `streaming-digest`.

Default ports:

- API + Blazor: `8080`
- Postgres: `5432`, internal by default
- Grafana: `3000`
- Prometheus: `9090`
- Loki: `3100`
- Tempo: `3200`
- OpenTelemetry Collector: `4317` gRPC, `4318` HTTP
- Ollama: `11434`
- Hangfire dashboard: API-hosted at `/admin/jobs`
- Aspire dashboard: local development only

Production should expose only the services intentionally reachable over Tailscale/reverse proxy. Postgres and internal service ports should remain on the Compose network unless explicitly needed.

## 4. Request/data flows

### 4.1 Channel ingestion flow

1. User adds or updates channel in Blazor admin UI.
2. API validates and stores channel configuration.
3. User triggers ingestion or scheduled Hangfire job starts.
4. Worker resolves channel metadata and videos using yt-dlp/adapter and optional YouTube API key.
5. Worker filters regular long-form public videos within configured max age or backfill parameters.
6. Worker skips already processed videos unless retry/reprocess requested.
7. Worker creates an ingestion run and per-video ingestion records.
8. Worker processes each video with configurable concurrency.

### 4.2 Video processing flow

1. Fetch metadata and description.
2. Best-effort fetch pinned comment.
3. Extract transcript/captions.
4. If no usable transcript, download temporary audio/video and run local audio-to-text.
5. Delete temporary media.
6. Detect author-provided chapters.
7. If chapters absent, chunk transcript deterministically and refine with local LLM.
8. Generate WebP screenshot per segment at configured timestamp offset.
9. Extract and normalize links from description and pinned comment.
10. Classify links with rules + local LLM + user correction examples.
11. Process repository links and website links.
12. Create or update search documents.
13. Generate embeddings for required content.
14. Record events, warnings, errors, and status.

### 4.3 Repository processing flow

1. Detect repository host and canonicalize URL.
2. Fetch repository metadata.
3. Fetch README.
4. Fetch LICENSE.
5. Check DeepWiki URL and reject placeholder page containing text such as "Index your code".
6. Store repository data and link to source video/link.
7. Embed README chunks.
8. Store LICENSE text without default embedding.

### 4.4 Website scraping flow

1. Confirm URL is allowed by robots.txt and rate limits.
2. Launch Crawlee/Playwright browser context.
3. Load first page only.
4. Extract title, description, OpenGraph/Twitter-card metadata, canonical URL, visible text.
5. Store extracted text/metadata.
6. Store raw HTML only if per-video debug capture is enabled.
7. Embed page text.

### 4.5 Search flow

1. User submits query and filters.
2. API normalizes query.
3. API generates query embedding through Semantic Kernel/Ollama.
4. API runs hybrid search using PostgreSQL text indexes and pgvector.
5. Ranking combines text score and vector score using configurable weights.
6. Results are shaped into result cards with parent video context.
7. API includes match explanations and snippets.
8. Blazor renders unified result list and filters.

### 4.6 Edit and re-embedding flow

1. User edits a field in modal.
2. API stores override and previous override value/version history.
3. API marks affected search documents stale.
4. Hangfire job regenerates embeddings using effective values: override if present, otherwise original scraped value.
5. Search results use effective values.

### 4.7 Matrix notification flow

1. Ingestion run completes or fails.
2. Worker writes run summary.
3. Worker queues notification request.
4. Matrix notifier formats summary.
5. Matrix notifier sends encrypted message to configured room ID.
6. Notification event/status is stored.

## 5. Technology choices

### 5.1 ASP.NET Core 10

Use ASP.NET Core 10 for API, auth, Blazor hosting, health checks, OpenTelemetry, and dependency injection.

### 5.2 Blazor WASM

Host Blazor WASM from the API. Benefits:

- Single public application endpoint.
- Simpler auth/cookie handling.
- Easier Tailscale/reverse-proxy configuration.

### 5.3 Hangfire

Use Hangfire with PostgreSQL storage for:

- Scheduled ingestion.
- Manual ingestion.
- Backfill jobs.
- Retryable video/link/repo jobs.
- Embedding regeneration.
- Matrix notification jobs.

Hangfire dashboard is API-hosted and linked in the Blazor admin UI.

### 5.4 Data access

Use a hybrid data access strategy:

- EF Core + Npgsql for core CRUD and migrations.
- Raw SQL/Dapper for search, pgvector queries, and tuned ingestion bulk operations.
- EF migrations plus raw SQL migrations for indexes, pgvector, full-text, and trigram support.

### 5.5 Microsoft Semantic Kernel

Use Semantic Kernel for:

- Embedding provider abstraction.
- Local LLM abstraction.
- Audio-to-text abstraction where compatible.

Adapters should hide concrete Ollama/whisper implementation details from application services.

### 5.6 Crawlee/Playwright

Use Crawlee/Playwright for website scraping because it supports modern websites and browser automation. Keep scraping first-page-only for MVP.

## 6. Observability architecture

### 6.1 Local development

Aspire dashboard shows:

- Service health.
- Logs.
- Traces.
- Metrics.
- Dependency graph.

### 6.2 Production

OpenTelemetry exporters send telemetry to OTel Collector.

Collector routes:

- Metrics to Prometheus.
- Logs to Loki.
- Traces to Tempo.

Grafana dashboards:

- API latency/errors.
- Worker job throughput/failures.
- Ingestion run stats.
- Embedding/audio-to-text latency.
- Browser scraping failures/rate limits.
- Matrix notification success/failure.
- PostgreSQL performance.

PostgreSQL stores:

- Domain ingestion events.
- Ingestion run summaries.
- Warning/error summaries.

Avoid storing every log line in PostgreSQL.

Retention:

- Logs/metrics/traces: 90 days.
- Ingestion run summaries: indefinite or configurable.
- Detailed ingestion events: default 90 days unless configured otherwise.

## 7. Security architecture

### 7.1 Authentication

- Single admin account.
- First startup reads username/password from environment variables.
- Password stored hashed in database.
- Forced password change if seeded from environment variable.
- Argon2id hashing.
- Secure HTTP-only cookies.
- CSRF protection for mutating endpoints.
- Login rate limiting.

### 7.2 Network access

- Designed for Tailscale access.
- No requirement for public internet exposure.
- Internal services remain on Compose network by default.
- TLS can be provided by reverse proxy/Tailscale if needed.

### 7.3 Secrets

Secrets include:

- Admin bootstrap credentials.
- Database password.
- YouTube API key, optional.
- Matrix bot credentials/token.
- Matrix recovery/session data.
- Ollama/LLM service credentials if any.

Use environment variables, Docker secrets, or host secret management. Do not store secrets in Git.

### 7.4 Matrix E2EE

Matrix notifier must persist crypto state. Losing this store may require device re-verification or may make historical encrypted messages unreadable by the bot.

Backup:

- Matrix crypto/session volume.
- Bot config.
- Room ID.

## 8. Failure handling

Principles:

- Fail per item, not per entire run, whenever possible.
- Continue processing remaining videos/links after a failure.
- Store failure reason, stage, retryability, and raw diagnostic summary.
- Provide retry buttons in admin UI.
- Notify summary includes failed/skipped counts.

Failure examples:

- Transcript unavailable and audio-to-text fails: video is ingested with missing transcript status and failure event.
- Pinned comment unavailable: warning only.
- Repo README unavailable: repo stored with missing README status.
- Website blocks scraper: link stored with scrape failure status.
- Embedding service unavailable: mark embeddings stale and retry later.
- Matrix send fails: notification failure stored and retryable.

## 9. Rate limiting and politeness

Implement configurable per-host limits for:

- YouTube metadata/transcript/comment access.
- Repository hosts.
- DeepWiki checks.
- Generic website scraping.

Defaults should be conservative.

Browser scraper must:

- Check robots.txt where applicable.
- Identify itself appropriately where possible.
- Avoid recursive crawling in MVP.
- Use bounded timeouts and page size limits.

## 10. Backup and restore

Back up:

- PostgreSQL database.
- Screenshot/media volume.
- Matrix crypto/session store.
- Configuration/secrets.

Restore procedure must validate:

- App can log in.
- Search works.
- Screenshots resolve.
- Matrix notifier can send encrypted test message.
- Embedding service can regenerate embeddings.

## 11. Aspire and Compose

Aspire is used for:

- Local service orchestration.
- Dependency wiring.
- Health checks.
- Local dashboard.
- Generating/publishing deployment artifacts, including Compose where practical.

Production uses Docker Compose with separate containers and shared `streaming-digest-*` naming.

Aspire AppHost should not be required as a production process unless explicitly chosen later.

## 12. Cross-platform development notes

Target development hosts:

- macOS ARM.
- Windows ARM.
- Linux deployment.

Local models must support CPU and, where possible, GPU acceleration. Because GPU support differs per OS/architecture, configuration must allow CPU fallback.
