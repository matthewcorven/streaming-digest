# Streaming Digest Product Requirements Document

Status: MVP scope agreed
Product: Streaming Digest
Primary user: single on-prem user
Target platform: on-prem Linux Docker Compose deployment; macOS ARM and Windows ARM development support where practical

## 1. Product summary

Streaming Digest is a self-hosted personal YouTube knowledge ingestion, search, and curation application.

It monitors a manually configured list of subscribed YouTube channels, ingests newly published long-form videos within a configurable lookback window, extracts transcripts, semantic segments, timecoded screenshots, external links, repository metadata, and website content, then stores searchable metadata and vector embeddings in PostgreSQL with pgvector.

The primary user experience is a Blazor WASM search interface where the user can search across video metadata, transcript segments, external links, repositories, scraped pages, and personal notes using hybrid text + semantic vector search. Results are ranked, explain why they matched, and link directly to the relevant YouTube timestamp, channel profile, code repository, website, or edit/note workflow.

The secondary user experience is operational: daily/manual ingestion summaries are sent over Matrix with end-to-end encryption, and the application exposes rich observability through Aspire locally and Prometheus/Grafana/Loki/Tempo in deployment.

## 2. MVP scope

This is a hard MVP: all items below are required for the first usable release.

### 2.1 Ingestion scope

MVP must support:

- Manual channel list management.
- Regular public long-form YouTube videos only.
- Exclusion of Shorts and private/member-only content.
- Configurable max-age lookback, default 30 days.
- User-triggered backfill with its own days and max-count parameters.
- OSS-first YouTube ingestion using yt-dlp and related tools.
- Optional YouTube Data API key for improved metadata/comment reliability.
- Metadata extraction:
  - YouTube video ID.
  - Channel/author name.
  - Channel ID and channel/profile URL.
  - Title.
  - Description.
  - Publish date.
  - Duration.
  - Video URL.
  - Thumbnail metadata.
- Transcript extraction:
  - Use existing YouTube captions/transcripts where available.
  - Automatically run local audio-to-text fallback for videos without usable transcript.
  - Delete temporary audio/video files after processing.
- Segment extraction:
  - Use author-provided chapters/timecodes when available.
  - Otherwise perform deterministic transcript chunking plus local LLM refinement of semantic segment boundaries and titles.
  - Default cap: maximum 60 segments per video.
  - Default target segment length: 2 to 5 minutes.
  - Segment cap is configurable as a user app setting.
- Screenshot extraction:
  - One WebP screenshot per chapter/semantic segment.
  - Default screenshot timestamp: segment start + 5 seconds.
  - Screenshot timing is configurable.
  - Store screenshot files on a mounted volume.
  - Store screenshot metadata/path in PostgreSQL.
- Link extraction:
  - Video description.
  - Pinned comment, best-effort.
  - Pinned-comment extraction failure must not fail video ingestion.
- Link normalization:
  - Follow redirects where safe.
  - Remove common tracking parameters.
  - Store original URL and normalized final URL.
- Link classification:
  - Keep all links.
  - Classify links as code repository, website/resource, ad/sponsor, affiliate, social, newsletter, course, merch, unknown, or other.
  - Use rules + required local LLM classification.
  - Learn over time from user corrections via deterministic domain/rule lists and few-shot examples.
- Repository ingestion:
  - GitHub.
  - GitLab.
  - Bitbucket.
  - Repository metadata.
  - README text.
  - LICENSE text.
  - DeepWiki URL if reachable and not the placeholder "Index your code" page.
- Website ingestion:
  - Crawlee/Playwright-based OSS CLI-driven browser scraping.
  - Respect robots.txt and per-host rate limits.
  - First page only.
  - Extract title, description, OpenGraph/Twitter-card metadata, and visible page text.
  - Store extracted text/metadata.
  - Do not store raw HTML by default.
  - Optional per-video debug capture of raw HTML is available in MVP.

### 2.2 Embedding scope

MVP must generate vector embeddings for:

- Video title and description.
- Chapter/segment titles.
- Transcript chunks.
- External link metadata.
- Scraped website page text.
- Repository README chunks.
- User notes.

MVP must store LICENSE text but does not embed LICENSE text by default.

MVP must use local OSS embeddings through Microsoft Semantic Kernel + Ollama.

Default embedding configuration:

- Configurable model name.
- Prefer `bge-m3` when available.
- Fall back/document `nomic-embed-text` as a simpler option.
- Store embedding model ID, dimensions, provider, and source content hash with each embedding.

### 2.3 Local LLM and audio-to-text scope

MVP requires a local LLM runtime.

Uses:

- Link classification.
- Semantic segment title/boundary refinement.
- Few-shot learning from classification corrections.

Implementation direction:

- Microsoft Semantic Kernel for model abstraction.
- Ollama for the local LLM and embeddings.
- Configurable model with documented minimum capability expectations.
- Recommended class: small local instruction model, e.g. Llama 3.1/3.2 8B-class or suitable Phi-class model depending on hardware.

Audio-to-text:

- Implement behind a Semantic Kernel-style audio-to-text abstraction, aligned with `AudioToTextClientBase` where compatible.
- Back with a local engine/model that runs on CPU or GPU.
- Prefer whisper.cpp if it provides the best local CPU/GPU and ARM portability.
- Temporary audio/video files are deleted after processing.

### 2.4 Search and curation scope

MVP must provide:

- Blazor WASM application hosted by ASP.NET Core API.
- REST API.
- Single-user login.
- Hybrid search combining text match and vector similarity.
- Configurable text/vector ranking weights.
- Unified ranked result list.
- Filters:
  - Channel.
  - Date range.
  - Result type.
  - Link classification.
  - Has transcript.
  - Has repo.
  - Has notes.
  - Ingestion status.
- Result unit is the best matching item with parent video context.
- Result cards show:
  - Match type and explanation.
  - Matched field/snippet.
  - Rank/score details sufficient for user trust.
  - Video title, author, publish date.
  - Link to watch YouTube video from start or timestamp.
  - Link to author/channel profile.
  - Link to repository or website when applicable.
  - Screenshot thumbnail when available.
  - Notes indicator.
  - Edit action.
  - Notes action.
- Edit modal supports overrides for:
  - Video title.
  - Video description.
  - Segment title.
  - Segment summary/description.
  - Transcript chunk text.
  - External link title.
  - External link description.
  - External link classification.
  - Repository metadata.
  - Note markdown.
- Original scraped values are preserved.
- Overrides are used for future embedding regeneration.
- Override version history records previous value and changed_at.
- Notes:
  - Private local notes.
  - Attach to video, segment, external link, and repository.
  - Markdown support using EasyMDE.

### 2.5 Admin and operations scope

MVP admin UI must support:

- Add/remove/pause channels.
- Trigger ingestion now.
- Trigger channel backfill with days/max-count.
- View last channel ingestion status.
- Retry failed video ingestion.
- Retry failed link/repository ingestion.
- Regenerate embeddings for one item.
- Regenerate all embeddings after model change.
- Purge screenshots for video/channel.
- Test Matrix notification.
- Test embedding service.
- Test Whisper/audio-to-text service.
- Link to Hangfire dashboard.
- Link to Grafana.
- Link to other observability dashboards where configured.

Deletion behavior:

- Deleting a channel can optionally delete all related videos, segments, links, notes, embeddings, and screenshots.
- Destructive deletes require confirmation.

### 2.6 Notifications scope

MVP must send Matrix notifications with E2EE.

Requirements:

- Dedicated Matrix bot account, separate from the user personal account.
- Separate `streaming-digest-matrix-notifier` container/service.
- Matrix crypto/session store persisted and backed up.
- Manual one-time login and verification using the user's Android Matrix client documented.
- Configurable encrypted room ID.
- Notifications sent for manual and scheduled ingestion runs by default.
- Notification behavior configurable in app settings.

Notification summary includes:

- Channels checked.
- New videos found.
- Videos ingested successfully.
- Videos failed/skipped.
- Transcripts found/missing.
- Repositories found.
- Link to web dashboard ingestion run.

### 2.7 Observability scope

MVP must include:

- OpenTelemetry instrumentation across API, worker, browser scraping, embedding calls, audio-to-text calls, Matrix notification, and database access.
- Aspire dashboard for local development/debugging.
- Production observability stack:
  - Prometheus.
  - Grafana.
  - Loki.
  - Tempo.
  - OpenTelemetry Collector.
- WASM app links to Grafana, Hangfire dashboard, and other configured operational endpoints.
- Domain events and warning/error summaries stored in PostgreSQL.
- Full logs stored in Loki.
- Metrics stored in Prometheus-compatible backend.
- Traces stored in Tempo.
- Telemetry retention: 90 days for logs, metrics, and traces.
- Ingestion run summaries retained indefinitely or until configured retention policy says otherwise.

### 2.8 Security scope

MVP must include:

- Single-user local login.
- Username/password seeded from environment variables at first startup, then stored hashed in DB.
- Argon2id password hashing.
- Secure cookies.
- CSRF protection for mutating endpoints.
- Login rate limiting.
- Forced password change if seeded from environment variable.
- Tailscale-oriented access model.
- Secrets configured through environment variables, Docker secrets, or equivalent.

### 2.9 Backup/restore scope

MVP documentation and implementation must cover backup/restore for:

- PostgreSQL database.
- Screenshot/media mounted volume.
- Matrix crypto/session store.
- Relevant app configuration/secrets.

## 3. Non-goals for MVP

The following are out of scope unless explicitly promoted later:

- YouTube OAuth subscription import.
- Unlimited historical ingestion without user-provided backfill bounds.
- Shorts support.
- Private/member-only video support.
- Full recursive website crawling.
- Repository source-code indexing beyond README/LICENSE/metadata.
- Multi-user collaboration.
- Public publishing/export workflows.
- Mobile-native application.

## 4. User journeys

### 4.1 Add and ingest channels

1. User logs in.
2. User opens Channels admin page.
3. User adds a YouTube channel URL or channel ID.
4. App resolves the canonical channel ID/name/profile URL.
5. User sets optional per-channel configuration.
6. User triggers ingestion or waits for schedule.
7. Worker checks videos within max-age window.
8. Worker processes unprocessed videos.
9. User views ingestion run details.

### 4.2 Search for remembered project idea

Example query: "code project that searches for project ideas not yet achieved across all of github"

1. User enters query.
2. API performs hybrid text/vector search.
3. Results include transcript segments, video metadata, repository README matches, scraped websites, and notes.
4. Results are ranked and show explanations.
5. User opens a timestamped YouTube link, repository, website, or note.
6. User optionally edits metadata or adds a note.
7. Modified fields trigger embedding regeneration using overrides.

### 4.3 Correct link classification

1. App classifies a link as sponsor/ad.
2. User opens edit modal and changes classification to repository/resource.
3. App stores correction history.
4. App updates deterministic classification rules and/or few-shot examples for future local LLM classification.
5. Affected embeddings/search index are updated where needed.

### 4.4 Manual backfill

1. User opens channel admin details.
2. User enters backfill days and max videos.
3. App queues a Hangfire backfill job.
4. Worker processes videos subject to configured concurrency/rate limits.
5. Matrix notification is sent when the run completes.

### 4.5 Daily notification

1. Scheduled ingestion run starts.
2. Worker checks channels and processes eligible videos.
3. Domain events and stats are recorded.
4. Matrix notifier sends encrypted summary to configured room.
5. User opens dashboard link from Android Matrix client.

## 5. Acceptance criteria

MVP is acceptable when:

- A user can configure at least one channel and run ingestion.
- Ingestion processes a long-form public video with transcript without manual intervention.
- Ingestion automatically transcribes a video without captions using local audio-to-text.
- Ingestion generates semantic segments and WebP screenshots.
- Ingestion extracts description and pinned-comment links best-effort.
- GitHub, GitLab, and Bitbucket repo links can be recognized and stored.
- README text is stored and embedded.
- LICENSE text is stored.
- DeepWiki URL is stored only when a non-placeholder page exists.
- A website link can be scraped one page deep with Crawlee/Playwright, stored, and embedded.
- Search returns hybrid ranked results with explanations and filters.
- Timestamped result links open YouTube at the correct time.
- User can edit all required override fields.
- Embedding regeneration uses overrides.
- User can create/edit/delete markdown notes using EasyMDE.
- Matrix E2EE notification reaches the configured encrypted room.
- Hangfire dashboard and Grafana links are visible from the app.
- Observability data appears in local Aspire dashboard and production observability stack.
- Backup/restore procedure is documented.

## 6. Risks and product tradeoffs

- This MVP is large and operationally complex.
- Matrix E2EE requires careful one-time device verification and durable crypto state.
- Whisper fallback can be CPU/GPU expensive for long videos.
- Semantic segmentation quality depends on local model capability.
- Pinned comments and YouTube scraping are inherently best-effort.
- Stealth browser scraping must be rate-limited and respectful.
- Observability must not compete with application queries; full logs/traces should remain outside primary app tables.
- Repository APIs differ; normalizing GitHub/GitLab/Bitbucket requires careful abstractions.

## 7. Legal/privacy constraints

Streaming Digest is intended for personal archival/search use on the user's own infrastructure.

Docs and UI should communicate:

- YouTube metadata, transcripts, screenshots, and video-derived artifacts may be subject to YouTube Terms of Service and copyright restrictions.
- Website scraping must respect robots.txt, rate limits, and applicable laws.
- Repository README/LICENSE content must preserve source URL and license context.
- User notes and search history may contain sensitive personal research and should remain private.
- Matrix bot credentials and crypto state must be protected and backed up.
