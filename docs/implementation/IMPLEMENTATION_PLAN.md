# Streaming Digest Implementation Plan

Status: Docs-only implementation blueprint

## Goal

Build the hard MVP for Streaming Digest: an on-prem ASP.NET Core 10 LTS + Blazor WASM + Aspire application that ingests YouTube channel content, extracts transcripts/segments/screenshots/links/GitHub repositories/web pages, generates local embeddings, provides hybrid search and curation, sends Matrix notifications, and exposes production-grade observability. Matrix E2EE, GitLab, and Bitbucket are MVP+.

## Product-scope anchor

The MVP is anchored on one killer journey: a user adds one public YouTube channel, waits for the scheduled ingestion run, searches for a vague project idea, and immediately finds the relevant video cluster with top-level metadata, warnings, and whatever timestamp/repository/website/note/related-item data is available at that time. Implementation choices should optimize this journey before broader ingestion/import scenarios.

MVP explicitly supports public YouTube channel URL/handle/channel ID input only. Logged-in YouTube subscription scraping/import, YouTube watch-history search, advanced query syntax, link-classification hide/show filters, granular search-history deletion, 500+ channel scale, MCP/CLI integrations, and other source imports are MVP+.

## MVP scope conformance checklist

Before implementation work is considered MVP-complete, every hard-MVP requirement from `docs/product/PRD.md`, `docs/architecture/ARCHITECTURE.md`, `docs/architecture/DATA_MODEL.md`, `docs/api/API_SPEC.md`, and `docs/operations/UPGRADE_PATHS.md` must be either implemented and verified or explicitly reclassified in the product docs. The implementation plan must not silently pull MVP+ work into MVP. In particular:

- Matrix MVP means unencrypted bot notifications to a configured room. Matrix E2EE, Android/device verification, and E2EE crypto-store readiness are MVP+.
- GitHub repository ingestion is MVP. GitLab, Bitbucket, repository PATs, and repository OAuth are MVP+.
- Link classification correction is MVP. Link-classification search filtering/hide-show behavior is MVP+.
- Recent-search clear-all is MVP. Granular per-query deletion is MVP+.
- Public YouTube channel URL/handle/channel ID input is MVP. Logged-in subscription import, watch-history import, and broader source imports are MVP+.

## Architecture summary

- ASP.NET Core 10 LTS is a hard requirement and hosts Blazor WASM.
- Worker service runs Hangfire jobs.
- PostgreSQL stores relational data, pgvector embeddings, Hangfire storage, and domain events.
- Microsoft Semantic Kernel abstracts Ollama embedding/local LLM and audio-to-text provider.
- Crawlee/Playwright performs first-page website scraping.
- Matrix notifier service sends MVP Matrix notifications without requiring E2EE; E2EE messaging, durable Matrix crypto state, and device verification are MVP+.
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
- `src/StreamingDigest.MatrixNotifier` - Matrix notification service; E2EE is MVP+.
- `src/StreamingDigest.Scraper` - Crawlee/Playwright scraper service, likely Node/TypeScript.
- `tests/StreamingDigest.UnitTests`
- `tests/StreamingDigest.IntegrationTests`

Verification:

- `dotnet build` succeeds.
- Aspire AppHost starts local dependencies.

### Task 0.2: Add baseline package dependencies

Add packages for:

- ASP.NET Core 10 LTS.
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

### Task 0.3: Add schema-validated application config file

Requirements:

- Use a JSON config file validated against a JSON Schema on startup.
- Treat environment variables as deployment/bootstrap inputs and secrets, not the primary mutable settings store.
- UI-editable settings persist to config file or database-backed app settings according to mutability and secret-sensitivity.
- Startup reports clear schema validation errors.

Verification:

- Invalid config fails startup with actionable message.
- UI setting changes survive container restart when persisted to the configured mutable store.

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

### Task 2.4: Implement first-run onboarding state

Requirements:

- Start in onboarding if setup is incomplete.
- Required before first ingestion: admin password setup/change, embedding model verification, local LLM verification, first public YouTube channel, and ingestion schedule confirmation.
- Default ingestion schedule is 6 AM local user time and configurable during first run.
- Audio-to-text, Matrix, and Grafana/observability verification contribute to full readiness but surface warnings instead of blocking basic search UI access.
- Each setup step supports live verification, inline retry, retained previous values, clear success state, and actionable failure messages.
- Post-login routing precedence: incomplete onboarding, last selected mode, dashboard summary after first daily run, then ingestion/new-videos digest.

Verification:

- Incomplete setup routes to onboarding.
- Core-value setup can proceed while Matrix/Grafana/Whisper warnings remain visible.
- Verified settings persist and pre-fill on retry.

### Task 2.5: Implement model discovery/download onboarding

Requirements:

- Display a hard-coded installation-configuration list of supported Hugging Face/Ollama/Whisper model IDs and download commands. Do not attempt live hardware-based model viability detection in MVP.
- First-run can trigger selected model download through an internal service HTTP API that executes configured CLI commands against a mounted model volume.
- User may alternatively provide an existing host model path that is mounted into the container, or follow displayed CLI commands manually.
- Provide a refresh button to detect completion after file-path, mounted-model, or command-line setup.
- Confirm before embedding model changes after initial setup; on confirmation invalidate old embeddings and queue regeneration.

Verification:

- Missing model shows options and command snippets.
- Inline download records verified model state.
- Embedding model switch asks confirmation and marks old embeddings invalid/stale.

## Phase 3: Channel management

### Task 3.1: Implement channel CRUD API

Endpoints from API spec:

- list/create/get/update/delete.

Verification:

- Integration tests cover channel lifecycle.

### Task 3.2: Implement channel management UI

UI supports:

- Add public YouTube channel URL/handle/channel ID, e.g. `https://www.youtube.com/@TonbisAIGarage`.
- Validate that the input is a supported YouTube channel source.
- Pause/resume.
- Edit settings.
- Delete with optional related data deletion.

Verification:

- Manual UI test with test channel.
- Non-YouTube and logged-in-only subscription import inputs are rejected or labeled MVP+.

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

### Task 4.3: Implement custom job progression and batch tracking

Use Hangfire OSS jobs plus application-owned progression/batch tracking in PostgreSQL. Do not depend on Hangfire Pro batches for MVP.

Retryable stage names:

- `metadata`
- `transcript`
- `audio_transcription`
- `segmentation`
- `screenshots`
- `link_extraction`
- `link_classification`
- `repository_metadata`
- `repository_readme`
- `repository_license`
- `deepwiki_check`
- `website_scrape`
- `search_documents`
- `embeddings`
- `notification`

Retry can operate at video, stage, external link occurrence, external resource, repository, search-document/embedding, and notification levels as needed.

Verification:

- Retry UI can select failed stages/items without Hangfire Pro.
- Old queued job/stage names can be mapped or cancelled/recreated safely.

### Task 4.4: Add admin run/retry endpoints

Endpoints:

- run all.
- run channel.
- backfill channel.
- retry failed item/video/link/repo.

Verification:

- Integration tests confirm job enqueue and status updates.

### Task 4.5: Implement rate-limit deferment service

Requirements:

- Persist host/dependency deferments in `rate_limit_deferments` for YouTube, repository hosts, DeepWiki, and website hosts.
- Workers check active deferments before starting host-scoped work.
- Resume after `Retry-After` when present, otherwise after the configured default delay.
- Dashboard and ingestion-run details show active deferments prominently.
- Matrix/web daily digest includes active deferments when configured.
- Manual clear endpoint is available for careful operator override.

Verification:

- Repository, DeepWiki, website, and YouTube rate-limit fixtures create deferments and prevent new host-scoped work until expiry/clear.
- Dashboard/API exposes active, expired, and cleared deferments.

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

### Task 5.4: Implement video idempotency and degraded-channel handling

Requirements:

- Normalize YouTube video URL by removing query string and use it as idempotency key, with YouTube video ID as canonical platform identifier.
- Normal daily ingestion skips already processed videos.
- Previously processed videos are reprocessed only through explicit user retry/reprocess actions.
- Adapter failures retry with exponential backoff for two retries, then circuit-break and mark channel degraded until a future daily run succeeds without failures.
- Failures without active retry may early-return for the affected item while allowing other items to continue.

Verification:

- Daily re-run does not duplicate or reprocess a processed video.
- Explicit retry processes selected failed stages/items.
- Repeated adapter failure marks channel degraded.

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

1. Download temporary audio/video into the service child `temp/` folder.
2. Enforce configurable temp-media quota, defaulted during first run to 50% of then-available free disk bytes.
3. Transcribe locally.
4. Delete temporary media after success and failure.
5. Run startup cleanup after a brief delay and post-daily-run cleanup.
6. Store transcript and cues.

Filename scheme:

- `{runId}/{videoId}/{stage}-{attempt}-{contentHashPrefix}.{ext}`

Verification:

- Test confirms temp file deletion after success and failure.
- Startup cleanup removes orphan temp files.
- Lost temp media causes the stage to repeat rather than corrupting state.

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
- Schema validation is the only MVP repair mechanism; invalid output is logged and written to stdout for development diagnostics, then deterministic chunks are used.
- Never regenerate segments during normal daily ingestion. Segment regeneration is explicit user action only. If regenerated segments would change embeddings, stage embedding updates for user approval and surface the pending approval on dashboard and daily report.

Verification:

- Unit test validates JSON parsing/fallback.
- Invalid LLM output logs diagnostic and keeps deterministic chunks.
- Explicit segment regeneration stages embedding updates pending approval.
- Integration test with local model optional.

### Task 7.4: Generate WebP screenshots

Rules:

- One per segment.
- Default timestamp: start + 5 seconds.
- Configurable offset.
- Store file on mounted volume.
- Store metadata/path in DB.
- If segments or screenshot offset change by explicit user action, purge/recreate screenshots immediately.

Verification:

- Test video fixture generates WebP under expected path.
- Metadata row created.
- Offset or segment-change request purges/recreates screenshots.

## Phase 8: Link extraction/classification

### Task 8.1: Extract description and pinned-comment links

Requirements:

- Description links required.
- Pinned comment best-effort.
- Early development decision: use `yt-dlp` for pinned comments if available/reliable; otherwise use public browser scrape where practical.
- Failure to fetch pinned comment is warning only.

Verification:

- Fixture description/comment produces normalized links.
- Pinned-comment failure records warning and does not fail video ingestion.

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

- GitHub for MVP.
- GitLab and Bitbucket are MVP+.

Verification:

- Unit tests for canonical URLs.

### Task 9.2: Implement repository metadata adapters

Use unauthenticated public REST APIs by default for GitHub. GitLab and Bitbucket are MVP+. PAT support is MVP+; OAuth is MVP++.

Fetch:

- owner/name.
- default branch.
- description.
- stars/forks where available.
- language/topics where available.
- license SPDX where available.

Rate limits:

- On 429/rate limit, pause all repository processing globally for that host. Defer all active jobs and prevent the next daily run from starting host-repository work until after all deferred jobs are completed. Resume at `Retry-After`, or after one hour when absent.
- Surface active deferment in dashboard and Matrix notification.

Verification:

- Fixture tests per host.
- Rate-limit fixture defers remaining work and resumes later.

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
- robots setting and per-domain override setting.
- debug raw HTML flag.
- timeout.

Supported:

- PDFs.
- JavaScript-rendered pages with Playwright JS enabled.
- CDN URLs.
- Displayed/visible text from HTML.
- Non-tracking redirects, preserving original and resulting URL.

Excluded:

- Login pages.
- Tracking redirects.
- Non-PDF file downloads.
- Hidden/invisible element text.
- Raw HTML by default.

Outputs:

- final URL.
- title.
- description.
- OpenGraph/Twitter metadata.
- visible text.
- robots allowed.
- debug raw HTML path optional.
- exclusion reason when skipped.

Verification:

- Scrapes local test page.
- Excluded URLs create partial failure records skipped from retry unless URL changes.

### Task 10.2: Add robots.txt and rate limiting

Requirements:

- Per-host rate limit.
- Respect robots.txt by default. If denied, skip scrape but store link.
- Per-domain user override in app configuration.
- First page only.

Verification:

- Local test robots.txt disallow case stores link and skips scrape.
- Per-domain override allows scrape in controlled fixture.

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

### Task 11.6: Implement recent-search storage and embeddings

Requirements:

- Store recent searches in PostgreSQL.
- Embed each search using the active embedding model.
- Store query text, searched_at, active text/vector weights, filters JSON, and embedding reference.
- Store MVP interaction events for clicked/opened results so they can boost future signal strength.
- Provide a recent-searches panel and a clear-all search-history action.
- Granular per-query deletion is MVP+.

Verification:

- Search query creates a recent-search row and embedding.
- Clear-all removes recent-search history.
- Opened result creates a user-signal event used by high-signal ranking.

### Task 11.7: Implement video-cluster aggregate embeddings

Requirements:

- Build `video_cluster_embeddings` from normalized child document embeddings that share provider, model, and dimensions.
- Store content hash, provider/model/dimensions, component weights, stale state, and operation provenance.
- Use aggregate cluster vectors for high-signal digest matching and coarse related-item discovery.
- Do not use aggregate cluster vectors as the only search index; fine-grained `search_documents` remain the primary search units.
- Mark cluster embeddings stale when child search documents, notes, overrides, or active embedding model changes require invalidation.

Verification:

- Integration test creates a cluster embedding after document embeddings exist.
- Editing a note/title/transcript marks only the affected document(s) and parent cluster aggregate stale.
- High-signal digest matching ignores mismatched provider/model/dimension vectors.

## Search performance targets

MVP corpus assumption is fewer than 500 videos in PostgreSQL, while design should remain reasonable up to about 2,000 videos. Show a spinner or progress state after 1 second.

Latency targets:

- Fewer than 500 videos in DB: P50 <= 2 seconds, P95 <= 5 seconds.
- Up to 2,000 videos in DB: P50 <= 3 seconds, P95 <= 10 seconds.

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

- Document score: `document_score = textWeight * normalizedTextScore + vectorWeight * normalizedVectorScore`.
- Video cluster score: `base = 0.65 * max(document_score) + 0.25 * average(top 3 document_scores) + 0.10 * coverage_score`, where `coverage_score = min(distinctMatchedDocumentTypes / 4, 1.0)`.
- Add `note_boost = 0.08` when cluster has a matching note.
- Add `interaction_boost = min(0.05, 0.01 * recent_open_count_for_cluster)`.
- Final `cluster_score = min(1.0, base + note_boost + interaction_boost)`.
- UI label is `Relative similarity`; it is a normalized vector rank score within current result set, with tooltip explaining that it is relative to the query/model/result set and not confidence.

Return:

- score.
- score components.
- matched fields.
- explanation.
- snippets.

Verification:

- Unit/integration tests validate ordering with fixed data.
- Tooltip text explains `Relative similarity` semantics.

### Task 12.4: Implement search UI

Features:

- Natural-language query box for MVP. Advanced query syntax is MVP+.
- Filters for channel, date range, result type, has transcript, has repo, has notes, and ingestion status. Link-classification hide/show filtering is MVP+.
- Global app setting for text/vector ranking weights.
- Video-clustered ranked list.
- Collapsed result card shows title, channel, publish date, note indicator/button, processing/stale/failed indicator, retry button when applicable, primary match, and score.
- Expanded result card shows all submatches, related/similar items from across the whole corpus with `Relative similarity` percentages, screenshot thumbnail, timestamp links, repository/website links, score components, and processing warnings. Related items render inside the same result container with border color/type variants.
- One video with many matching segments appears as one result, e.g. `12 matches inside`, with best timestamp directly reachable.
- Recent-searches panel with clear-all action.

Verification:

- Manual end-to-end search scenario passes.
- A vague query returns one video cluster with timestamp/repo/website/note matches and related item percentages.

### Task 12.5: Implement cluster ranking and similarity percentages

Requirements:

- API clusters document matches by video. Multiple result clusters must not reference the same video.
- Cluster title is video override title when present, otherwise original scraped title.
- Cluster score is a weighted aggregate score over submatches with note/user-signal boosts.
- Related items are drawn from across the whole corpus and expose `Relative similarity` percentages.
- Daily-digest high-signal matching uses a configurable global threshold, default 80%, against recent-search embeddings.

Verification:

- Unit tests cover weighted cluster-score calculation.
- Integration test confirms multiple segment matches from one video produce one cluster.
- High-signal query fixture returns expected items over the configured threshold.

### Task 12.6: Implement dashboard daily digest and pending-action inbox

Requirements:

- Dashboard priority order is daily digest, search launchpad, then pending-action inbox.
- Daily digest shows new videos ingested, new repositories found, new websites/resources found, high-signal matches similar to recent searches, and failed/skipped items.
- Pending-action inbox orders pending approvals, failed ingestion, degraded channels, deferred rate limits, stale embeddings, model/service warnings, new digest items, recent-search matches, and storage/retention warnings.
- Post-login routing follows the product rule: incomplete onboarding, last selected mode, dashboard summary after first daily run, then ingestion/new-videos digest.

Verification:

- Fixture ingestion run renders digest sections in the correct priority order.
- High-signal recent-search matches appear with relative-similarity percentages and links to available timestamp/repository/website artifacts.
- Pending-action fixture renders retry/approve/test actions without requiring log inspection.

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

MVP notes are not a primary note-taking product surface. They exist so note content is embedded, evaluated in search weighting, and reflected in the parent video-cluster aggregate.

Verification:

- Note creates search document and embedding.
- Clearing/deleting note updates the note embedding/search document and parent video-cluster aggregate so repeated searches reflect live ranking.

### Task 13.3: Implement Blazor modals

- Edit modal with tabbed groups of fields.
- Lightweight notes modal opened contextually once an item appears in search results.
- EasyMDE markdown editor if cheap; rich notes UX is not an MVP focus.
- Link-classification correction feedback: `Future similar links will use this correction`, shown on save and when viewing corrected items later.

Verification:

- User can edit metadata and note; search reflects update after embedding regeneration.
- Note boosts parent item ranking.
- Classification correction displays feedback and influences future prompt examples/rules.

## Phase 14: Matrix notifications

### Task 14.1: Select Matrix SDK/implementation

Choose a mature OSS Matrix SDK/service approach. Prefer .NET until feature or stability issues justify Node/Rust/Python or another mature SDK. MVP sends normal unencrypted Matrix messages. E2EE support, Android/device verification, and durable Matrix crypto state are MVP+.

Requirements:

- Dedicated bot account.
- Manual login/token/configuration flow appropriate for the selected SDK.
- Configurable room ID.
- Unencrypted test send for MVP readiness.
- E2EE/encrypted room support, Android/device verification, and E2EE crypto-store backup/restore readiness are MVP+.

Verification:

- Bot can send an unencrypted test message for MVP. Encrypted test message applies only when E2EE is later enabled.

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

Notification content includes:

- New videos ingested.
- New repositories found.
- New websites/resources found.
- Items similar to recent searches.
- Failed/skipped items.
- Active rate-limit deferments where relevant.
- Link to web dashboard ingestion run.

Configurable app settings.

Verification:

- Manual run completion sends Matrix summary. Encrypted/E2EE summary is MVP+.
- Scheduled run notification includes high-signal matches similar to recent searches.

### Task 14.4: Implement notification audit and outbox dispatch

Requirements:

- Persist notification attempts/results in `notifications`, including provider, target, status, payload/rendered body, provider message ID, attempt count, retry time, and error summary.
- Use `outbox_messages` for reliable dispatch of Matrix notifications and other side effects.
- Failed notification sends are retryable and visible in ingestion-run details/admin UI.
- Notification status is linked to the originating operation and ingestion run.

Verification:

- Successful send creates notification audit row with provider message ID.
- Simulated notifier failure creates retryable notification/outbox state without failing the whole ingestion run.
- Retried notification updates attempt count and final status.

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

Policy:

- Included in Compose.
- Default-on for localhost development.
- Default-off elsewhere unless enabled during first run or toggled on demand in UI.
- UI links render only when observability is enabled.
- When disabled, the API container/reverse proxy serves placeholder observability pages on the usual routes/ports with instructions to re-enable. When enabled, the same API/reverse-proxy paths route to real observability services.
- Retention selected by first-run free space: 90 days when > 5 GB, 30 days when > 1 GB, disabled with warning otherwise.

Verification:

- `docker compose up` starts stack.
- Grafana dashboards reachable through API/reverse-proxy routes when enabled.
- Disabled mode shows API-served placeholder guidance instead of broken links.

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

### Task 15.5: Implement retention and cleanup jobs

Requirements:

- Enforce telemetry retention selected during first run: 90 days when free space is > 5 GB, 30 days when > 1 GB, otherwise disabled with warning.
- Clean up detailed domain/ingestion events according to configured retention while preserving long-lived ingestion run summaries.
- Purge screenshots and raw HTML debug captures from mounted volumes when corresponding records are purged/deleted.
- Preserve raw transcripts and screenshots indefinitely unless explicitly purged/deleted.

Verification:

- Retention job deletes expired detailed events but not retained run summaries.
- Channel/video delete with media purge removes screenshot/debug files from disk.
- Low-disk first-run fixture disables or lowers telemetry retention with a visible warning.

## Phase 16: Admin operations

Recommended MVP concurrency defaults:

- Channels processed concurrently: `1`.
- Videos per channel concurrently: `1`.
- Screenshots concurrently: `1`.
- Embedding batch size: `16` short documents or adaptive token-budget batching.
- Website scrapes: global `2`, per-host `1`.
- Repository API calls: global `2`, per-host `1`.
- Whisper jobs: `1` globally.
- Local LLM classification/segmentation jobs: `1` globally.

Normal user actions should be provided contextually where they are useful: retry video, retry repo/link, regenerate embeddings for a visible item, purge screenshots for a video/channel, test Whisper/audio-to-text, test Matrix, run ingestion now, and run backfill.

The single Admin page owns: change model, toggle observability, backup, upgrade/maintenance, and global settings.

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
- Matrix bot session/config store for the selected MVP SDK; E2EE crypto/session store is MVP+.
- Grafana/Prometheus/Loki/Tempo data as needed.

Verification:

- Restart preserves data.

### Task 17.3: Implement MVP backup/restore

Backup:

- PostgreSQL.
- screenshots/media volume.
- Matrix bot session/config store for the selected MVP SDK; E2EE crypto/session store is MVP+.
- app config/secrets.

MVP UI:

- Provide a backup button that triggers a server-side backup to a configured folder.
- Offer optional download after the backup completes successfully.
- Recommend backup before migration/upgrade.
- Scheduled backups, CLI backup, and advanced restore workflows are MVP+.

Restore validation:

- login works.
- search works.
- screenshots load.
- Matrix test send succeeds; encrypted send applies when E2EE is enabled.
- embedding test works.

### Task 17.4: Document and test restore runbook

Requirements:

- Document restore procedure for PostgreSQL, screenshots/media, Matrix bot session/config, app config, and secrets.
- Restore to a fresh Compose stack during validation rather than only checking that backup files exist.
- Record backup artifact metadata and verification status in `backup_artifacts`/maintenance operations.
- Keep polished automated restore UI, scheduled backups, and CLI backup/restore as MVP+.

Verification:

- Restore dry run validates login, search, screenshots, Matrix test send, and embedding test.
- Restore docs clearly distinguish MVP Matrix bot session/config from MVP+ E2EE crypto-store restore verification.

### Task 17.5: Implement upgrade and migration policy

Requirements:

- Follow `docs/operations/UPGRADE_PATHS.md`.
- Track `appVersion`, `dbSchemaVersion`, `configSchemaVersion`, and `deploymentSchemaVersion`.
- Distinguish safe app-only upgrades, app upgrades with data migration, derived-data regeneration upgrades, deployment/Compose migrations, and high-risk infrastructure migrations.
- Versioned Compose tags.
- EF migrations run on startup, with workers blocked until schema compatibility is confirmed.
- UI/docs recommend backup before migration and require backup for high-risk infrastructure migrations.
- Migration failure is surfaced clearly and does not silently corrupt state.
- Add an Admin UI Upgrade & Maintenance panel showing versions, upgrade status, backup status, migration preview, service compatibility, derived-data status, risk level, and post-upgrade checklist.

Verification:

- Startup applies migration in integration test.
- Pre-migration backup recommendation appears in upgrade docs/UI.
- Worker refuses to process jobs when DB/config/deployment versions are incompatible.
- Upgrade & Maintenance panel renders risk level and required next action.

## Phase 18: REST API contract conformance

Requirements:

- Implement every MVP endpoint and response shape in `docs/api/API_SPEC.md`, including auth/CSRF, operations, ingestion, search, recent searches, video details, edit/override, notes, embeddings, screenshots, repositories, external resources/link occurrences, admin health/tests, backups, and maintenance endpoints.
- Endpoints whose behavior is explicitly MVP+ must be omitted from MVP docs or documented as MVP+ rather than implemented accidentally.
- Mutation endpoints return stale search-document IDs, stale cluster IDs, and queued operations where relevant.
- Errors use consistent RFC 7807-style problem details.
- Batch retry/regenerate/delete endpoints return per-item acceptance/rejection details.

Verification:

- API conformance test enumerates `docs/api/API_SPEC.md` MVP endpoints and verifies route existence/auth behavior.
- Search response fixture matches video-cluster contract and excludes MVP+ link-classification filters.
- Admin health/test, maintenance, backup, screenshot, repository, and external-resource endpoint smoke tests pass.

## Phase 19: End-to-end acceptance tests

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

Given a video description includes a GitHub repo:

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
- Dedicated bot sends summary to configured Matrix room; E2EE is MVP+.
- User sees message on Android Matrix client.

### Scenario 18.7: Observability

- API request trace visible.
- Worker ingestion trace visible.
- Logs in Loki.
- Metrics in Prometheus/Grafana.
- Domain event in Postgres.

### Scenario 18.8: Killer journey

Given a user adds one public YouTube channel and leaves the default scheduled run enabled:

- Scheduled ingestion runs.
- Search for a vague project idea returns the relevant video cluster.
- The cluster exposes top-level video metadata, warning state, and whatever timestamp/repository/website/note/related-item data is available at that time.
- Related items show visible `Relative similarity` percentages.
- Failures are prominent and retryable without reading logs.

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
11. Video-cluster aggregate embeddings, high-signal matching, and daily digest dashboard.
12. Matrix notification audit/outbox; E2EE is MVP+.
13. Observability, retention, deployment, backup/restore, and upgrade hardening.
14. REST API contract conformance and end-to-end acceptance tests.

Even though all are hard MVP, this sequence produces testable increments.

## Quality gates

Before declaring MVP complete:

- `dotnet test` passes.
- Integration tests run against PostgreSQL + pgvector.
- Compose stack starts cleanly.
- Local model health checks pass.
- Audio-to-text health check passes.
- Matrix test notification succeeds; encrypted test notification is MVP+.
- Search latency is acceptable on representative dataset.
- Ingestion handles partial failures and retries.
- Backup/restore dry run documented and tested.
- Daily digest dashboard and pending-action inbox satisfy priority/order requirements.
- Notification audit/outbox retry behavior is verified.
- Video-cluster aggregate embeddings are generated, invalidated, and used for high-signal matching.
- Rate-limit deferments are persisted, enforced, surfaced, and clearable.
- Retention/cleanup jobs handle domain events, telemetry policy, screenshots, and raw debug captures.
- API contract conformance tests pass for all MVP endpoints.

## Open implementation decisions

These are implementation-time choices, not product-scope blockers:

- Exact Matrix SDK/service technology.
- Exact whisper.cpp/Semantic Kernel adapter strategy.
- Exact Ollama LLM model default per hardware.
- HNSW vs IVFFlat pgvector index based on installed pgvector version and expected dataset size.
- Whether Crawlee/Playwright runs in worker container or separate scraper container.
