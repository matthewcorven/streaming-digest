# Streaming Digest Implementation Plan

Status: Docs-only implementation blueprint

## Goal

Build the hard MVP for Streaming Digest: an on-prem ASP.NET Core 10 + Blazor WASM + Aspire application that ingests YouTube channel content, extracts transcripts/segments/screenshots/links/repositories/web pages, generates local embeddings, provides hybrid search and curation, sends Matrix E2EE notifications, and exposes production-grade observability.

## Architecture summary

- ASP.NET Core 10 API hosts Blazor WASM.
- Worker service runs Hangfire jobs.
- PostgreSQL stores relational data, pgvector embeddings, Hangfire storage, and domain events.
- Microsoft Semantic Kernel abstracts Ollama embedding/local LLM and audio-to-text provider.
- Crawlee/Playwright performs first-page website scraping.
- Matrix notifier service handles E2EE messaging and crypto state.
- Aspire orchestrates local development and produces Compose deployment artifacts.
- Production observability uses OTel Collector, Prometheus, Grafana, Loki, and Tempo.

## Phase 0: Repository and solution setup

### Task 0.1: Create solution structure

Create projects:

- `src/StreamingDigest.AppHost` - Aspire AppHost.
- `src/StreamingDigest.Api` - ASP.NET Core API + Blazor WASM host.
- `src/StreamingDigest.Web` - Blazor WASM client if using hosted template split.
- `src/StreamingDigest.Worker` - Hangfire worker.
- `src/StreamingDigest.Domain` - domain entities/value objects.
- `src/StreamingDigest.Application` - use cases/services/contracts.
- `src/StreamingDigest.Infrastructure` - EF Core, Dapper, external adapters.
- `src/StreamingDigest.MatrixNotifier` - Matrix E2EE notification service.
- `src/StreamingDigest.Scraper` - Crawlee/Playwright scraper service, likely Node/TypeScript.
- `tests/StreamingDigest.UnitTests`
- `tests/StreamingDigest.IntegrationTests`

Verification:

- `dotnet build` succeeds.
- Aspire AppHost starts local dependencies.

### Task 0.2: Add baseline package dependencies

Add packages for:

- ASP.NET Core 10.
- Blazor WASM hosted setup.
- Aspire orchestration.
- Npgsql EF Core provider.
- pgvector Npgsql support.
- Dapper.
- Hangfire.AspNetCore and Hangfire.PostgreSql.
- OpenTelemetry.
- Serilog or Microsoft.Extensions.Logging structured logging.
- Argon2id password hashing library.
- Microsoft Semantic Kernel.
- EasyMDE frontend package.

Verification:

- Restore succeeds.
- No package downgrade warnings.

## Phase 1: Database foundation

### Task 1.1: Create PostgreSQL migration baseline

Implement schema from `docs/architecture/DATA_MODEL.md`.

Start with:

- `app_users`
- `app_settings`
- `channels`
- `videos`
- `ingestion_runs`
- `ingestion_items`
- `domain_events`

Then add content/search tables in later tasks.

Verification:

- Integration test applies migrations to test PostgreSQL.
- Required extensions installed: `vector`, `pg_trgm`.

### Task 1.2: Add EF Core DbContext and repositories

Implement:

- `StreamingDigestDbContext`
- entity configurations
- timestamp handling
- optimistic update conventions where useful

Verification:

- CRUD integration test for channel/video.

### Task 1.3: Add raw SQL migration support

Implement a migration pattern for:

- pgvector indexes.
- full-text indexes.
- trigram indexes.
- search SQL functions/views.

Verification:

- Integration test confirms indexes/extensions exist.

## Phase 2: Authentication and app shell

### Task 2.1: Implement bootstrap admin user

Requirements:

- Read bootstrap username/password from environment variables at first startup.
- Hash with Argon2id.
- Store in `app_users`.
- Set `must_change_password=true`.

Verification:

- Startup creates user once.
- Password is not stored plaintext.

### Task 2.2: Implement login/logout/change password

Requirements:

- Secure HTTP-only cookies.
- CSRF protection for mutations.
- Login rate limiting.
- Forced password change when seeded from env.

Verification:

- Auth integration tests pass.
- Mutating endpoint rejects unauthenticated request.

### Task 2.3: Build Blazor app shell

Pages:

- Login.
- Dashboard.
- Search.
- Channels.
- Ingestion Runs.
- Admin/Settings.

Verification:

- User can log in and navigate.

## Phase 3: Channel management

### Task 3.1: Implement channel CRUD API

Endpoints from API spec:

- list/create/get/update/delete.

Verification:

- Integration tests cover channel lifecycle.

### Task 3.2: Implement channel management UI

UI supports:

- Add channel URL/ID.
- Pause/resume.
- Edit settings.
- Delete with optional related data deletion.

Verification:

- Manual UI test with test channel.

## Phase 4: Hangfire and ingestion runs

### Task 4.1: Configure Hangfire with PostgreSQL

Requirements:

- API hosts dashboard at `/admin/jobs`.
- Worker processes jobs.
- Dashboard requires authentication.

Verification:

- Test job executes.
- Dashboard link works.

### Task 4.2: Implement ingestion run records

Support:

- scheduled run.
- manual run.
- backfill run.
- per-item statuses.

Verification:

- Starting manual run creates `ingestion_runs` and `ingestion_items`.

### Task 4.3: Add admin run/retry endpoints

Endpoints:

- run all.
- run channel.
- backfill channel.
- retry failed item/video/link/repo.

Verification:

- Integration tests confirm job enqueue and status updates.

## Phase 5: YouTube ingestion adapter

### Task 5.1: Implement yt-dlp metadata adapter

Extract:

- channel ID/name/profile URL.
- video ID/title/description/publish date/duration.
- chapters if available.
- captions metadata if available.

Verification:

- Adapter contract test uses recorded fixture JSON.

### Task 5.2: Implement optional YouTube API adapter

Use optional API key for:

- improved channel metadata.
- comments/pinned comment where feasible.

Verification:

- Works when key absent.
- Uses API when configured.

### Task 5.3: Implement long-form and max-age filtering

Rules:

- Exclude Shorts.
- Regular public long-form videos only.
- Default max age 30 days.
- Backfill uses separate days/max-count.

Verification:

- Unit tests for filtering.

## Phase 6: Transcript and audio-to-text

### Task 6.1: Implement caption/transcript ingestion

Store:

- `video_transcripts`
- `transcript_cues`

Verification:

- Fixture transcript stored with timestamps.

### Task 6.2: Implement audio-to-text provider abstraction

Define application interface aligned with Semantic Kernel audio-to-text abstraction.

Methods:

- `TranscribeAsync(audioInput, options)`
- returns full text and timestamped cues.

Verification:

- Fake provider test.

### Task 6.3: Implement local whisper service adapter

Preferred engine: whisper.cpp if compatible.

Requirements:

- CPU fallback.
- GPU configurable where available.
- Model configurable.
- Internal API or CLI wrapper.

Verification:

- Transcribe bundled tiny audio fixture.

### Task 6.4: Implement automatic fallback

If no usable YouTube transcript:

1. Download temporary audio/video.
2. Transcribe locally.
3. Delete temporary media.
4. Store transcript and cues.

Verification:

- Test confirms temp file deletion after success and failure.

## Phase 7: Segmentation and screenshots

### Task 7.1: Store author chapters as segments

Map yt-dlp chapters to `segments`.

Verification:

- Fixture chapters create ordered segments.

### Task 7.2: Implement deterministic transcript chunking

Rules:

- Target 2-5 minutes per chunk.
- Max 60 segments default.
- Preserve cue boundaries.

Verification:

- Unit tests for long transcript chunking and cap.

### Task 7.3: Implement LLM semantic refinement

Using Semantic Kernel + Ollama:

- Input deterministic chunks/cues.
- Output JSON segment boundaries/titles/summaries.
- Validate output against schema.
- Fall back to deterministic chunks if LLM output invalid.

Verification:

- Unit test validates JSON parsing/fallback.
- Integration test with local model optional.

### Task 7.4: Generate WebP screenshots

Rules:

- One per segment.
- Default timestamp: start + 5 seconds.
- Configurable offset.
- Store file on mounted volume.
- Store metadata/path in DB.

Verification:

- Test video fixture generates WebP under expected path.
- Metadata row created.

## Phase 8: Link extraction/classification

### Task 8.1: Extract description and pinned-comment links

Requirements:

- Description links required.
- Pinned comment best-effort.
- Failure to fetch pinned comment is warning only.

Verification:

- Fixture description/comment produces normalized links.

### Task 8.2: Normalize URLs

Implement:

- tracking parameter removal.
- redirect resolution where safe.
- domain extraction.
- original/final URL preservation.

Verification:

- Unit tests for common tracking parameters.

### Task 8.3: Rule-based classification

Classify known patterns:

- code repository.
- ad/sponsor.
- affiliate.
- social.
- newsletter.
- course.
- merch.
- website_resource.

Verification:

- Unit tests for representative URLs.

### Task 8.4: Local LLM classification

Use:

- local model via Semantic Kernel/Ollama.
- JSON schema output.
- corrections as few-shot examples.

Verification:

- Invalid LLM output falls back safely.
- Correction history influences prompt construction.

### Task 8.5: Classification correction workflow

When user edits classification:

- Store override.
- Store `classification_corrections`.
- Update rule/few-shot source.
- Mark relevant search documents stale.

Verification:

- Integration test correction changes future classification prompt examples.

## Phase 9: Repository ingestion

### Task 9.1: Implement repository host detection

Support:

- GitHub.
- GitLab.
- Bitbucket.

Verification:

- Unit tests for canonical URLs.

### Task 9.2: Implement repository metadata adapters

Fetch:

- owner/name.
- default branch.
- description.
- stars/forks where available.
- language/topics where available.
- license SPDX where available.

Verification:

- Fixture tests per host.

### Task 9.3: Fetch README and LICENSE

Store README and LICENSE as `repository_documents`.

Verification:

- README present/missing cases.
- LICENSE present/missing cases.

### Task 9.4: Check DeepWiki URL

For repo owner/name:

- Build `https://deepwiki.com/{owner}/{repo}`.
- Fetch page.
- Store URL only if reachable and not placeholder text such as "Index your code".

Verification:

- Placeholder page is rejected.
- Existing fixture is accepted.

## Phase 10: Website scraping

### Task 10.1: Build Crawlee/Playwright scraper service

Inputs:

- URL.
- robots setting.
- debug raw HTML flag.
- timeout.

Outputs:

- final URL.
- title.
- description.
- OpenGraph/Twitter metadata.
- visible text.
- robots allowed.
- debug raw HTML path optional.

Verification:

- Scrapes local test page.

### Task 10.2: Add robots.txt and rate limiting

Requirements:

- Per-host rate limit.
- Respect robots.txt where applicable.
- First page only.

Verification:

- Local test robots.txt disallow case.

### Task 10.3: Store scraped page results

Map output to `scraped_pages`.

Verification:

- Integration test stores page and debug path when enabled.

## Phase 11: Search documents and embeddings

### Task 11.1: Implement effective value service

Effective value = override if present else original.

Verification:

- Unit tests for all editable fields.

### Task 11.2: Build search document generator

Generate documents for:

- video metadata.
- segment titles/summaries.
- transcript chunks.
- external link metadata.
- scraped page text.
- repository README chunks.
- notes.

Verification:

- Integration test creates expected document types.

### Task 11.3: Implement Semantic Kernel embedding provider

Provider:

- Ollama endpoint.
- configurable model.
- dimensions detection/validation.

Verification:

- Test embedding service endpoint with sample text.

### Task 11.4: Store embeddings in pgvector

Requirements:

- content hash.
- provider/model/dimensions.
- idempotent regeneration.

Verification:

- Re-running embedding job does not duplicate unchanged embeddings.

### Task 11.5: Implement stale embedding regeneration

Triggers:

- override edit.
- note edit.
- model change.
- failed embedding retry.

Verification:

- Editing a title marks document stale and regeneration clears stale flag.

## Phase 12: Hybrid search

### Task 12.1: Implement full-text/trigram search SQL

Search over `search_documents`.

Verification:

- Partial query matches known fixture.

### Task 12.2: Implement vector search SQL

Use pgvector distance with configured model.

Verification:

- Similar query returns expected fixture document.

### Task 12.3: Implement hybrid ranking

Formula:

- `combined_score = textWeight * normalizedTextScore + vectorWeight * normalizedVectorScore`

Return:

- score.
- score components.
- matched fields.
- explanation.
- snippets.

Verification:

- Unit/integration tests validate ordering with fixed data.

### Task 12.4: Implement search UI

Features:

- query box.
- filters.
- unified ranked list.
- result card.
- timestamped YouTube links.
- repository/website links.
- author channel link.
- screenshot thumbnail.
- score explanation.

Verification:

- Manual end-to-end search scenario passes.

## Phase 13: Notes and edit modals

### Task 13.1: Implement override APIs

Support all candidates:

- video title/description/author.
- segment title/summary.
- transcript cue text.
- external link title/description/classification.
- repository metadata.

Verification:

- History stores previous value and changed_at.

### Task 13.2: Implement notes APIs

CRUD notes for:

- video.
- segment.
- external link.
- repository.

Verification:

- Note creates search document and embedding.

### Task 13.3: Implement Blazor modals

- Edit modal.
- Notes modal.
- EasyMDE markdown editor.

Verification:

- User can edit metadata and note; search reflects update after embedding regeneration.

## Phase 14: Matrix E2EE notifications

### Task 14.1: Select Matrix SDK/implementation

Choose mature OSS Matrix SDK/service approach with E2EE support and durable crypto store.

Requirements:

- dedicated bot account.
- manual login.
- Android client verification.
- encrypted room ID config.

Verification:

- Bot can send encrypted test message.

### Task 14.2: Build Matrix notifier service

Internal API:

- send ingestion summary.
- health check.
- test notification.

Verification:

- API test queues test notification and receives success status.

### Task 14.3: Integrate ingestion notifications

Send by default for:

- manual runs.
- scheduled runs.

Configurable app settings.

Verification:

- Manual run completion sends encrypted summary.

## Phase 15: Observability

### Task 15.1: Add OpenTelemetry instrumentation

Instrument:

- API requests.
- DB calls.
- Hangfire jobs.
- ingestion stages.
- embedding calls.
- LLM calls.
- audio-to-text calls.
- scraper calls.
- Matrix sends.

Verification:

- Local Aspire dashboard shows traces/logs/metrics.

### Task 15.2: Add production observability Compose services

Services:

- OTel Collector.
- Prometheus.
- Grafana.
- Loki.
- Tempo.

Verification:

- `docker compose up` starts stack.
- Grafana dashboards reachable.

### Task 15.3: Store domain events and warning/error summaries

Do not store every log line in Postgres.

Verification:

- Failed scrape creates domain event and Loki log.

### Task 15.4: Add UI observability links

Blazor admin page links to:

- Hangfire.
- Grafana.
- Prometheus if configured.
- Loki/Tempo through Grafana preferably.

Verification:

- Links render from settings.

## Phase 16: Admin operations

Implement UI/API for:

- run ingestion now.
- run channel backfill.
- retry failed video.
- retry failed link/repo.
- regenerate item embeddings.
- regenerate all embeddings after model change.
- purge screenshots for video/channel.
- test Matrix notification.
- test embedding service.
- test audio-to-text service.

Verification:

- Each action enqueues job or returns clear health result.

## Phase 17: Deployment and backup

### Task 17.1: Aspire-to-Compose deployment

Requirements:

- Compose project/base name: `streaming-digest`.
- Containers named `streaming-digest-*`.
- One command starts all containers.
- Internal-only ports for internal services.

Verification:

- Fresh host can start stack from documented command.

### Task 17.2: Configure volumes

Volumes:

- Postgres data.
- screenshots/media.
- optional raw HTML debug capture.
- Matrix crypto/session store.
- Grafana/Prometheus/Loki/Tempo data as needed.

Verification:

- Restart preserves data.

### Task 17.3: Document backup/restore

Backup:

- PostgreSQL.
- screenshots/media volume.
- Matrix crypto/session store.
- app config/secrets.

Restore validation:

- login works.
- search works.
- screenshots load.
- Matrix encrypted test sends.
- embedding test works.

## Phase 18: End-to-end acceptance tests

### Scenario 18.1: Captioned video ingestion

Given a configured channel with a recent long-form public video with captions:

- metadata stored.
- transcript stored.
- segments generated.
- screenshots generated.
- links extracted/classified.
- embeddings generated.
- search finds transcript segment.

### Scenario 18.2: No-caption video ingestion

Given a recent long-form public video without captions:

- temp audio/video downloaded.
- local transcription runs.
- temp files deleted.
- transcript stored.
- search finds transcript text.

### Scenario 18.3: Repository link

Given a video description includes a GitHub/GitLab/Bitbucket repo:

- repo stored.
- README stored and embedded.
- LICENSE stored.
- DeepWiki checked.
- result card links repo and parent video.

### Scenario 18.4: Website link

Given a video includes a non-ad website:

- first page scraped.
- visible text embedded.
- result card links website and parent video.

### Scenario 18.5: Edit and notes

- User edits transcript or title.
- Override history records previous value.
- Embedding regenerates using override.
- User adds EasyMDE note.
- Search finds note.

### Scenario 18.6: Matrix notification

- Manual ingestion completes.
- Dedicated bot sends encrypted summary to configured room.
- User sees message on Android Matrix client.

### Scenario 18.7: Observability

- API request trace visible.
- Worker ingestion trace visible.
- Logs in Loki.
- Metrics in Prometheus/Grafana.
- Domain event in Postgres.

## Implementation sequencing recommendation

Build in vertical slices after the foundation:

1. Auth + channel CRUD + Hangfire.
2. Basic yt-dlp metadata ingestion.
3. Transcript ingestion + search documents + embeddings.
4. Basic search UI.
5. Segmentation + screenshots.
6. Link/repo ingestion.
7. Website scraping.
8. Local LLM classification/semantic segmentation.
9. Whisper fallback.
10. Notes/edit/re-embedding.
11. Matrix E2EE.
12. Observability/deployment hardening.

Even though all are hard MVP, this sequence produces testable increments.

## Quality gates

Before declaring MVP complete:

- `dotnet test` passes.
- Integration tests run against PostgreSQL + pgvector.
- Compose stack starts cleanly.
- Local model health checks pass.
- Audio-to-text health check passes.
- Matrix encrypted test notification succeeds.
- Search latency is acceptable on representative dataset.
- Ingestion handles partial failures and retries.
- Backup/restore dry run documented and tested.

## Open implementation decisions

These are implementation-time choices, not product-scope blockers:

- Exact Matrix SDK/service technology.
- Exact whisper.cpp/Semantic Kernel adapter strategy.
- Exact Ollama LLM model default per hardware.
- HNSW vs IVFFlat pgvector index based on installed pgvector version and expected dataset size.
- Whether Crawlee/Playwright runs in worker container or separate scraper container.
