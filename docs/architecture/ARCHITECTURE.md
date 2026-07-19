# Streaming Digest Technical Architecture

Status: MVP architecture agreed
Target runtime: ASP.NET Core 10, Blazor WASM (PWA-enabled), Microsoft Fluent UI Components, Aspire, Docker Compose, PostgreSQL + pgvector

## 1. Architecture goals

Streaming Digest must be:

- Self-hosted on an on-prem Linux Docker host.
- Started with a single Docker Compose command generated or maintained from Aspire deployment artifacts.
- Observable locally through Aspire and in deployment through Prometheus/Grafana/Loki/Tempo/OpenTelemetry Collector.
- Private by default and suitable for Tailscale-only access.
- Single-user authenticated.
- PWA-enabled for an installable, app-like UX across desktop and mobile platforms from a single codebase.
- Capable of local embeddings, local LLM inference, and local audio-to-text.
- Robust against partial ingestion failures, explicit retries, idempotent daily ingestion, and deferred processing under rate limits.
- Designed for hybrid text/vector search over YouTube-derived knowledge artifacts.

## 2. Logical services

### 2.1 API and Blazor host: `streaming-digest-api`

Responsibilities:

- Hosts ASP.NET Core 10 REST API.
- Hosts the Blazor WebAssembly (WASM) UI as static files served directly from the API project.
- The UI is built with **Microsoft Fluent UI Blazor components** (`Microsoft.FluentUI.AspNetCore.Components` NuGet package).
- The UI runs **entirely client-side** — **no server-side rendering (SSR)**. The API serves the compiled WASM static assets and the app bootstraps in the browser.
- The WASM app is **PWA-enabled**: installable, app-like experience delivered via a web app manifest and service worker, both served as static assets from the API project alongside the WASM files.
- All UI↔backend communication is via **HTTP API calls only**; there is no SignalR circuit or server-side rendering pipeline.
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
- Model management: execute configured CLI download/use commands against a mounted model volume, including user-provided host model paths mounted into the container.

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

- Dedicated Matrix notification service.
- Sends ingestion/digest notifications to the configured room ID so the user can open the web UI from Android over Tailscale.
- E2EE, Matrix crypto/session persistence, and manual device verification are MVP+.

Prefer a .NET implementation until features or stability require a non-.NET SDK/service. Keep this separate from the API/worker if Matrix dependencies or state isolation justify it.

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
6. Worker skips already processed videos unless retry/reprocess is explicitly requested. A processed video URL should not be reprocessed during normal daily ingestion.
7. Worker creates an ingestion run and per-video ingestion records.
8. Worker processes each video with configurable concurrency.

### 4.2 Video processing flow

1. Fetch metadata and description.
2. Best-effort fetch pinned comment.
3. Extract transcript/captions.
4. If no usable transcript, download temporary audio/video into the service child `temp/` folder and run local audio-to-text.
5. Delete temporary media after success/failure; startup and post-run cleanup jobs remove leftovers. Lost temp files after a container crash cause the affected processing step to repeat.
6. Detect author-provided chapters.
7. If chapters absent, chunk transcript deterministically and refine with local LLM. Segments are stable by default and regenerated only on explicit user request.
8. Generate WebP screenshot per segment at configured timestamp offset. If segments or screenshot offset change by explicit request, screenshots are purged/recreated immediately.
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
5. Ranking combines text score and vector score using configurable weights, then aggregates document matches into video cluster search results.
6. Video cluster scores use weighted aggregate submatch scores, note boosts, interaction boosts, and coverage signals.
7. UI `Relative similarity` percentage is a normalized vector rank score within the current result set and includes a tooltip explaining that it is relative to the query/model/result set and not confidence.
8. API includes match explanations, score components, snippets, and related-item percentages.
9. Blazor renders video-clustered result cards and filters.

### 4.6 Edit and re-embedding flow

1. User edits a field in modal.
2. API stores override and previous override value/version history.
3. Affected search documents become stale by derivation — stored content hash no longer matches the Effective Value (ADR-0001).
4. Hangfire job reprocesses embeddings using effective values: override if present, otherwise original scraped value.
5. Search results use effective values.

### 4.7 Matrix notification flow

1. Ingestion run completes or fails.
2. Worker assembles and stores the run-scoped Digest (ADR-0006) — one assembly, two renderings.
3. Worker queues notification request.
4. Matrix notifier renders the summary as an excerpt of the stored Digest, so Matrix and dashboard never disagree. One narrow exception (ADR-0006 amendment): the dashboard's Digest section re-derives the active-deferments subsection from live state at render time; Matrix keeps the stored snapshot, and all other Digest fields stay stored on both surfaces.
5. Matrix notifier sends the message to the configured room ID (unencrypted in MVP; E2EE is MVP+).
6. Notification event/status is stored on the user-visible Notification record; outbox messages are internal plumbing only.

### 4.8 Idempotency and retry flow

- Daily ingestion uses the normalized YouTube video URL without query string as the idempotency key, with YouTube video ID as canonical platform identity.
- Already processed videos are skipped unless the user explicitly Reprocesses the video (full pipeline, bypassing the guard) or Retries failed stages/items. There are exactly two user-facing re-run verbs (ADR-0002): Retry for failed/deferred work, Reprocess for succeeded work. Reprocess eligibility means the pipeline completed — any status other than Core-Stage failure, including `processed_with_warnings` — and resets Retry Budgets; it also re-evaluates scrape-exclusion policy against the live site, while Retry leaves exclusions alone (ADR-0014).
- Failures without an active retry may short-circuit the affected item early while allowing other items to continue.
- Retry uses Hangfire OSS jobs plus application-owned progression/batch tracking in PostgreSQL, defaults to failed stages/items only, and supports user-selected all/one/multiple retryable operations. Do not depend on Hangfire Pro batches for MVP. A Retry Budget bounds attempts: 2 automatic backoff retries plus 5 manual Retries per item-stage, then the item becomes permanently failed until Reprocessed.
- External adapter failures use exponential backoff for two retries, then circuit-break the affected channel into the stored Degraded state (ADR-0003): skipped by scheduled runs but probed once per run with a single metadata fetch, clearing on success; Paused channels are never probed, and an active Deferment pauses the failure counter.

### 4.9 Deferred rate-limit flow

- Repository/API rate limits defer remaining processing instead of failing the whole run.
- Resume after the `Retry-After` value when present, otherwise after one hour.
- While a deferment is active, the dashboard shows it prominently and Matrix sends a notification when configured.

## 4.10 Search UX and dashboard conventions

- Search result success target: the intended recalled video cluster should appear in the top 3 results for the representative vague-query corpus.
- Collapsed result cards have two equal jobs: help the user decide whether to expand and provide immediate jumps to available timestamp/repository/website artifacts.
- Incomplete videos are visible in search with warning badges rather than hidden.
- Related items are drawn from across the whole corpus and rendered inside the same result container with border color/type variants.
- The dashboard priority is daily digest, search launchpad, then pending-action inbox. Pending-action ordering is pending approvals, failed ingestion, degraded channels, deferred rate limits, stale embeddings, model/service warnings, new digest items, recent-search matches, and storage/retention warnings.

## 4.11 Runtime configuration ownership

The configuration split follows `docs/operations/UPGRADE_PATHS.md`: Docker environment variables and secrets are for bootstrap/secrets/service wiring/runtime environment/mounted volume paths; schema-validated JSON config is for durable runtime/deployment configuration and first-run outputs; PostgreSQL app settings are for user-facing product behavior, onboarding/readiness state, operational state, and domain data.

## 5. Technology choices

### 5.1 ASP.NET Core 10

Use ASP.NET Core 10 for API, auth, Blazor hosting, health checks, OpenTelemetry, and dependency injection.

### 5.2 Blazor WASM

Host Blazor WASM from the API. Benefits:

- Single public application endpoint.
- Simpler auth/cookie handling.
- Easier Tailscale/reverse-proxy configuration.

**Component library:** Microsoft Fluent UI Blazor components via the `Microsoft.FluentUI.AspNetCore.Components` NuGet package. Fluent UI provides the design system, theming, and all standard UI controls (grids, forms, dialogs, navigation, etc.) so no additional CSS framework is needed.

**Hosting model:**

- **No server-side rendering (SSR).** The UI is compiled to WebAssembly and runs entirely in the browser.
- The API project serves the WASM app as **static files** only — there is no Blazor Server circuit, no interactive server-side rendering, and no SignalR connection for UI state.
- All backend communication is via **HTTP API calls** from the WASM client to the ASP.NET Core API endpoints.

**Development reference:** The `fluentui-blazor` skill is available for Fluent UI component usage patterns, theming guidance, and integration best practices during development.

**PWA:**

Streaming Digest is a **Progressive Web App from the very start of UI implementation** — PWA is embraced as a first-class design input, not retrofitted later.

Rationale:

- **Aligned UI/UX across platforms.** One codebase delivers a consistent experience on desktop, mobile, and tablet without per-OS app builds.
- **Installable, app-like experience.** The app installs to home screen / app shelf and launches in its own window with platform-native feel (standalone display mode, app icon, splash screen).
- **Single codebase for all OS targets.** No separate Electron wrapper, mobile app, or store distribution pipeline.

In scope for MVP:

- Web app manifest (name, icons, start_url, display mode, theme/background colors).
- Installability on supported platforms (Chrome/Edge desktop and Android; Add-to-Home-Screen on iOS).
- App icons and splash screens, including maskable icons for Android adaptive icons.
- Service worker registration for **lifecycle and install support** — the worker exists and is kept current from day one, but is not yet an offline engine.
- Responsive, mobile-first layout and platform-appropriate UX, leveraging PWA capabilities (display-mode detection, share target, app shortcuts, etc.) as needed for great UX on each platform.

MVP+ (explicitly deferred):

- **Full offline mode**: offline data caching of search results and artifacts, offline search, and background sync of user actions (notes, edits, approvals). The service worker foundation laid in MVP makes this an incremental addition later rather than a rework.

Development reference:

- The `pwa-development` skill is installed and is the **authoritative reference** for PWA patterns (manifest fields, service worker registration, caching strategies) — prefer it over assumptions about PWAs.
- https://whatpwacando.today/ is the capability reference for what PWAs can do on each platform.

Compatibility: PWA fits naturally with the declared stack — Blazor WASM publishes `blazor.webassembly.js` plus `service-worker.js` and `manifest.json` as static assets from the WASM project, which the API already serves as static files under the no-SSR hosting model. HTTPS is satisfied by the Tailscale/reverse-proxy deployment path (and localhost during development).

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

- Logs/metrics/traces: 90 days when first-run free space is greater than 5 GB, 30 days when greater than 1 GB, otherwise disabled with warning.
- Ingestion run summaries: indefinite or configurable.
- Detailed ingestion events: default 90 days unless configured otherwise.

### 6.3 Observability deployment policy

The observability stack is included in Compose, default-on for localhost development, and default-off elsewhere unless enabled during first run or toggled on demand in the UI. Observability dashboard links render only when enabled. When observability is disabled, the API container/reverse proxy should serve friendly placeholder pages for observability routes/ports explaining that observability is disabled and how to re-enable it. When observability is enabled, the same API/reverse-proxy paths route to the real observability services.

Retention is selected during first run from available free space: 90 days if free space is greater than 5 GB, 30 days if greater than 1 GB, otherwise disabled with a prominent warning.

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

### 7.4 Matrix E2EE MVP+

Matrix E2EE is MVP+. When enabled later, the Matrix notifier must persist crypto state. Losing this store may require device re-verification or may make historical encrypted messages unreadable by the bot.

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

- Check robots.txt where applicable. If disallowed, skip scraping but store the link. Per-domain user overrides are allowed in app configuration.
- Identify itself appropriately where possible.
- Avoid recursive crawling in MVP.
- Use bounded timeouts and page size limits.
- Support PDFs, JavaScript-rendered pages, CDN URLs, and displayed/visible HTML text extraction.
- Exclude login pages, tracking redirects, non-PDF file downloads, hidden/invisible text, and raw HTML content by default.
- Allow non-tracking redirects and store both original and resulting URLs, with the result URL available as the override/canonical URL.
- Treat excluded scrape attempts as partial failures that are skipped from retry unless the URL changes.

Pinned-comment extraction is best-effort. MVP should determine during early development whether `yt-dlp` can provide pinned comments reliably; if not, use public browser scrape where practical.

Repository fetching defaults to unauthenticated public REST APIs for GitHub in MVP. GitLab and Bitbucket are MVP+. User-provided PATs are MVP+, OAuth is MVP++.

DeepWiki detection is MVP-simple: store the URL only when the fetch returns HTTP 200 and the response does not contain `Index your code`. DeepWiki is a host scope like any other: a 429 defers remaining checks in the run rather than failing them; negative outcomes (no page/placeholder) re-check on Repository Reprocess; a stored reachable URL is never re-verified in MVP.

### 9.1 Recommended MVP concurrency defaults

Safe starting defaults for small on-prem hosts:

- Channels processed concurrently: `1` scheduled, `1` manual/backfill unless user raises it.
- Videos per channel concurrently: `1`.
- Screenshots concurrently: `1` per worker, because ffmpeg/browser work is CPU and I/O heavy.
- Embedding batch size: `16` short documents or adaptive token-budget batching; one embedding worker by default.
- Website scrapes: global `2`, per-host `1`.
- Repository API calls: global `2`, per-host `1`, with rate-limit deferment.
- Whisper jobs: `1` globally by default.
- Local LLM classification/segmentation jobs: `1` globally by default.

These defaults prioritize reliability over throughput and should be configurable after the MVP works.

## 10. Backup and restore

Back up:

- PostgreSQL database.
- Screenshot/media volume.
- Matrix crypto/session store when E2EE is enabled after MVP.
- Configuration/secrets.

Restore procedure must validate:

- App can log in.
- Search works.
- Screenshots resolve.
- Matrix notifier can send a test message; encrypted test send applies when E2EE is enabled after MVP.
- Embedding service can regenerate embeddings.

### 10.1 Backup, migration, and upgrade policy

MVP provides a UI backup button. Automated scheduled backup jobs, CLI backup commands, and advanced restore workflows are MVP+.

Upgrades use versioned Compose tags and EF migrations on startup, with a clear recommendation to take a backup before migration. Detailed upgrade categories, version tracking, edge cases, and UI requirements live in `docs/operations/UPGRADE_PATHS.md`.

The upgrade system tracks `appVersion`, `dbSchemaVersion`, `configSchemaVersion`, and `deploymentSchemaVersion` so the app can distinguish app-only upgrades from deployment/Compose migrations and high-risk infrastructure migrations. Workers must not process jobs until DB/config/deployment compatibility checks pass.

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
