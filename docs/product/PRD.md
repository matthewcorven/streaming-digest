# Streaming Digest Product Requirements Document

Status: MVP scope agreed
Product: Streaming Digest
Primary user: single on-prem user
Target platform: on-prem Linux Docker Compose deployment; macOS ARM and Windows ARM development support where practical

## 1. Product summary

Streaming Digest is a self-hosted personal YouTube video knowledge base.

It monitors a manually configured list of public YouTube channels, ingests newly published long-form videos within a configurable lookback window, extracts transcripts, semantic segments, timecoded screenshots, external links, GitHub repository metadata, website content, recent searches, and personal notes, then stores searchable metadata and vector embeddings in PostgreSQL with pgvector.

The primary user experience is a Blazor WASM search interface where the user can search across video metadata, transcript segments, external links, GitHub repositories, scraped pages, recent searches, and personal notes using hybrid text + semantic vector search. Results are clustered by video, ranked by weighted aggregate score, explain why they matched, show related matches across the whole corpus with relative-similarity percentages, and link to whatever useful artifacts are available for that video at search time, such as a YouTube timestamp, channel profile, code repository, website, or note workflow.

The secondary user experience is operational and mobile-friendly: daily/manual ingestion summaries are sent over Matrix so the user can open the web UI from Android over Tailscale, and the application exposes rich observability through Aspire locally and Prometheus/Grafana/Loki/Tempo in deployment. Matrix end-to-end encryption is MVP+.

## 2. MVP scope

This is a hard MVP: all items below are required for the first usable release.

Product priority order is: search, daily digest, notes/search curation, ingestion observability/admin, then repository/website enrichment. The dashboard priority order is daily digest, search launchpad, then pending-action inbox.

### 2.1 Primary MVP use case

The MVP is centered on one killer journey:

> A user adds one YouTube channel, waits for the scheduled ingestion run, searches for a vague project idea, and immediately finds the relevant video cluster with top-level metadata, warning state, and whatever useful artifacts are available at that time for that video, such as timestamps, repositories, websites, notes, screenshots, and related items.

This journey is the product-scope anchor. Search/recall and discovery have equal product weight. Synthesis/research-map experiences and daily monitoring are useful side effects but not primary MVP product goals. Features outside the anchor may still be required for hard MVP when they strengthen ingestion quality, search trust, correction, notifications, or operational reliability, but logged-in YouTube account features and broader source imports are MVP+.

### 2.2 Ingestion scope

MVP must support:

- Manual channel list management.
- Add-channel MVP input is a public YouTube channel URL, handle, or channel ID, such as `https://www.youtube.com/@TonbisAIGarage`.
- MVP is optimized for one configured channel, unbounded channel history over time, roughly five new ingested videos per day, unknown user-triggered backfill, about 150 ingested videos per month, and about 1,800 per year. Multi-channel scale beyond the first channel remains supported by design but is not the MVP optimization target; 500+ channels are MVP+.
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
  - GitLab and Bitbucket are MVP+.
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

### 2.3 Embedding scope

MVP must generate vector embeddings for:

- Video title and description.
- Chapter/segment titles.
- Transcript chunks.
- External link metadata.
- Scraped website page text.
- Repository README chunks.
- User notes.
- Recent searches.

User-authored notes are searchable weighting content, not a major MVP product surface. A note attached to a video, segment, repository, or link is embedded and evaluated for search weighting, and its presence/content can affect the parent video cluster score. Recent searches are a major product primitive: they provide UI convenience, long-term user-interest memory, digest subscription signals, and ranking personalization. They are stored in PostgreSQL, embedded with the active embedding model, visible in a recent-searches panel, and clearable as a whole by the user. Granular per-query deletion is MVP+.

MVP must support similarity discovery: search results show related items with `Relative similarity` percentages; the daily digest can include new videos/items similar to recent searches; and high-signal candidates are items above a configurable global similarity threshold, with new installs defaulting to 70%, against recent-search embeddings. Notes and clicked/opened results boost future signal strength.

MVP must store LICENSE text but does not embed LICENSE text by default.

MVP must use local OSS embeddings through Microsoft Semantic Kernel + Ollama.

Default embedding configuration:

- Configurable model name.
- Prefer `bge-m3` when available.
- Fall back/document `nomic-embed-text` as a simpler option.
- Store embedding model ID, dimensions, provider, and source content hash with each embedding.

### 2.4 Local LLM and audio-to-text scope

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

### 2.5 Search and curation scope

MVP must provide:

- Blazor WASM application hosted by ASP.NET Core API.
- REST API.
- Single-user login.
- Hybrid search combining text match and vector similarity.
- Configurable text/vector ranking weights exposed as a global app setting and applied to subsequent or active searches.
- Unified ranked result list clustered by video.
- Filters:
  - Channel.
  - Date range.
  - Result type.
  - Has transcript.
  - Has repo.
  - Has notes.
  - Ingestion status.
- Link-classification filtering and hide/show-by-category behavior are MVP+. Classification correction remains MVP because corrections improve future classification and ranking quality.
- Search box MVP supports natural-language text only. Advanced query syntax such as `repo:`, `channel:`, `has:notes`, `type:segment`, exact-phrase operators, and date expressions are MVP+.
- Result unit is one clustered video result. Multiple clusters must not reference the same video. Repositories or websites linked from multiple videos may appear in multiple video clusters.
- Cluster title is the video override title when present, otherwise the original scraped title.
- Cluster score is a weighted aggregate score over the cluster's matches, with note/user-signal boosts.
- One video with many matching segments appears as one result, e.g. "12 matches inside", with the best timestamp directly reachable.
- Result cards show:
  - Collapsed state: title, channel, publish date, note indicator/button, processing/stale/failed indicator, retry button when applicable, primary match, and score.
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
  - Expanded state: all submatches, related/similar items from across the whole corpus with relative-similarity percentages, screenshot thumbnail, timestamp links, repository/website links, score components, and processing warnings. Related items appear in the same result container, visually distinguished by border color and type.
  - No separate result detail page is required for MVP; result expansion and edit/note modals carry the interaction.
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
  - Attach to video, segment, external link, and repository once the target appears in search results.
  - MVP note UX is intentionally lightweight; notes exist so note content is embedded and evaluated in search/ranking, not to make notes a primary curation workflow.
  - Markdown support using EasyMDE is acceptable if cheap, but a rich note-taking surface is not required.
  - Notes boost their parent item based on note content/presence; if a note is cleared, the note embedding and video-cluster aggregate are updated so repeated searches reflect live state.
- Edit UI uses a well-organized modal with tabbed groups of fields rather than one giant form.
- Link-classification corrections show "Future similar links will use this correction" when saved and when the corrected item is later viewed.

### 2.6 Admin and operations scope

MVP admin UI must support:

- Add/remove/pause channels.
- Add-channel form initially requires only a public YouTube URL/handle/channel ID and validates that the URL is a supported YouTube channel source.
- Trigger ingestion now.
- Trigger channel backfill with days/max-count.
- View last channel ingestion status.
- Retry failed video ingestion.
- Retry failed link/repository ingestion.
- Reprocess one item (full pipeline; embeddings regenerate as a consequence).
- Reprocess all embeddings after embedding-model change.
- Purge screenshots for video/channel.
- Test Matrix notification.
- Test embedding service.
- Test Whisper/audio-to-text service.
- Link to Hangfire dashboard.
- Link to Grafana.
- Link to other observability dashboards where configured.
- Ingestion run details show timeline by stage, per-video status, failures with retry buttons, extracted links/repos/websites, transcript status, screenshot status, embedding status, and logs/trace links.
- Retry defaults to failed stages/items only, but the user can select all, one, or multiple retryable operations.

Deletion behavior:

- Deleting a channel can optionally delete all related videos, segments, links, notes, embeddings, and screenshots.
- Destructive deletes require confirmation.

### 2.7 Notifications scope

MVP must send Matrix notifications. Matrix E2EE is MVP+.

Requirements:

- Dedicated Matrix bot account, separate from the user personal account.
- Separate `streaming-digest-matrix-notifier` container/service.
- Matrix bot session/config store persisted and backed up for the selected MVP SDK.
- Manual Matrix bot login documented for MVP.
- Configurable room ID.
- E2EE crypto/session store persistence, Android client/device verification, and encrypted rooms/E2EE are MVP+.
- Notifications sent for manual and scheduled ingestion runs by default.
- Notification behavior configurable in app settings.

Notification summary includes:

- Channels checked.
- New videos found.
- Videos ingested successfully.
- Videos failed/skipped.
- Transcripts found/missing.
- Repositories found.
- Websites/resources found.
- High-signal matches similar to recent searches, including the matching recent search, absolute-similarity percentage (a fixed cosine bar, distinct from the rank-relative `Relative similarity` shown in search), timestamp when available, and repo/website links when available.
- Link to web dashboard ingestion run.

The web daily digest page includes new videos ingested, new repositories found, new websites/resources found, items similar to recent searches, and failed/skipped items.

### 2.8 Observability scope

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
- Telemetry retention follows the first-run disk policy: 90 days when free space is greater than 5 GB, 30 days when greater than 1 GB, otherwise disabled with warning.
- Ingestion run summaries retained indefinitely or until configured retention policy says otherwise.

### 2.9 Security scope

MVP must include:

- Single-user local login.
- Username/password may be created through the first-run setup UI when no app user exists.
- Optional bootstrap credentials may still be seeded from environment variables at first startup, then stored hashed in DB.
- Argon2id password hashing.
- Secure cookies.
- CSRF protection for mutating endpoints.
- Login rate limiting.
- Forced password change if seeded from environment variable.
- Tailscale-oriented access model.
- Secrets configured through environment variables, Docker secrets, or equivalent.

### 2.10 First-run and setup scope

MVP first-run onboarding distinguishes core value from full operational hardening:

- If no app user exists, anonymous users land on `/setup` to create the first local account before any authenticated workflow begins.
- If a bootstrap admin user was created from environment variables, the app routes that user through the forced password change path after first sign-in.
- The app starts in onboarding if readiness is incomplete after the user account step is satisfied.
- Required before first ingestion: first user creation or bootstrap-password rotation, embedding model verification, local LLM verification, first public YouTube channel, and ingestion schedule confirmation.
- Audio-to-text/Whisper verification is required for full setup completeness and for no-caption video support, but captioned-video ingestion may still proceed with a prominent warning if it is unavailable.
- Matrix bot login/verification and room send are required for full notification readiness, but missing Matrix configuration should block notifications, not basic search UI access. Matrix end-to-end encryption is MVP+.
- Grafana/observability endpoint verification is required for full operational readiness, but missing dashboard links should surface as warnings, not block search UI access.
- Each setup step provides live verification, inline retry, retained previously-entered values, clear success state, and actionable failure messages.
- Default ingestion schedule is 6 AM in the user's local time and is configurable during first run.
- Scheduled ingestion runs pause during an Embedding Transition (ADR-0011); a single catch-up run fires on transition completion.
- Until the first ingestion run completes with at least one video, the search page redirects to a waiting state with a run-now action — the flagship feature's first impression is never an unexplained void. A zero-video first run keeps the waiting state with backfill guidance.
- Post-login routing precedence is: forced password change when required, incomplete onboarding, last selected mode, dashboard summary after the first daily run, then ingestion/new-videos digest.

### 2.11 Backup/restore scope

MVP documentation and implementation must cover backup/restore for:

- PostgreSQL database.
- Screenshot/media mounted volume.
- Matrix bot session/config store for the selected MVP SDK; E2EE crypto/session store is MVP+.
- Relevant app configuration/secrets.

## 3. Non-goals for MVP

The following are out of scope unless explicitly promoted later:

- YouTube OAuth subscription import.
- Logged-in YouTube subscription scraping/import.
- Search of the user's whole YouTube watch history.
- Unlimited historical ingestion without user-provided backfill bounds.
- Shorts support.
- Private/member-only video support.
- Full recursive website crawling.
- Repository source-code indexing beyond README/LICENSE/metadata.
- Multi-user collaboration.
- Public publishing/export workflows.
- Mobile-native application.
- MCP server and CLI integrations for external AI agents/harnesses.

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
3. Results include video-clustered matches from transcript segments, video metadata, repository README matches, scraped websites, recent-search similarity, and notes.
4. Results are ranked by weighted aggregate score and show explanations, score components, and exact related-item percentages.
5. User expands the clustered result to see all submatches and opens a timestamped YouTube link, repository, website, or note.
6. User optionally edits metadata or adds a note from modals.
7. Modified fields trigger embedding regeneration using overrides and notes boost the parent result.

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
4. Matrix notifier sends summary to configured room, including new videos, repos, websites/resources, failed/skipped items, and high-signal matches similar to recent searches. E2EE is MVP+.
5. User opens dashboard link from Android Matrix client.

## 5. Acceptance criteria

MVP is acceptable when:

- A user can add one public YouTube channel and run ingestion.
- A user can wait for the default scheduled run, search a vague project idea, and find the relevant video cluster in the top 3 results with top-level metadata and whatever timestamp/repository/website/note/related-item data is available at that time.
- Ingestion processes a long-form public video with transcript without manual intervention.
- Ingestion automatically transcribes a video without captions using local audio-to-text.
- Ingestion generates semantic segments and WebP screenshots.
- Ingestion extracts description and pinned-comment links best-effort.
- GitHub repo links can be recognized and stored; GitLab and Bitbucket are MVP+.
- README text is stored and embedded.
- LICENSE text is stored.
- DeepWiki URL is stored only when a non-placeholder page exists.
- A website link can be scraped one page deep with Crawlee/Playwright, stored, and embedded.
- Search returns video-clustered hybrid ranked results with explanations, filters, score components, incomplete-processing warning badges, and related-item relative-similarity percentages.
- Timestamped result links open YouTube at the correct time.
- The user can find not only the one or two items they had in mind but also related items across the whole corpus with a visible relative-similarity percentage.
- User can edit all required override fields.
- Embedding regeneration uses overrides.
- User can create/edit/delete markdown notes using EasyMDE.
- Matrix notification reaches the configured room; Matrix E2EE is MVP+.
- Daily digest supports the expected user behavior: open high-signal items, open new videos from selected channels, and use the digest as a reading queue.
- Hangfire dashboard and Grafana links are visible from the app when enabled/configured.
- Observability data appears in local Aspire dashboard and production observability stack.
- Backup/restore procedure is documented.

## 6. Risks and product tradeoffs

- This MVP is large and operationally complex.
- Matrix E2EE, when promoted after MVP, requires careful one-time device verification and durable crypto state.
- Whisper fallback can be CPU/GPU expensive for long videos.
- Semantic segmentation quality depends on local model capability.
- Pinned comments and YouTube scraping are inherently best-effort.
- Stealth browser scraping must be rate-limited and respectful.
- Observability must not compete with application queries; full logs/traces should remain outside primary app tables.
- Repository APIs differ; GitHub normalization is MVP, while GitLab/Bitbucket normalization is MVP+.

## 7. Legal/privacy constraints

Streaming Digest is intended for personal archival/search use on the user's own infrastructure. MVP legal/product UX uses minimal disclaimers in docs/settings rather than prominent first-run or per-source acknowledgements.

Docs and UI should communicate:

- YouTube metadata, transcripts, screenshots, and video-derived artifacts may be subject to YouTube Terms of Service and copyright restrictions.
- Website scraping must respect robots.txt, rate limits, and applicable laws.
- Repository README/LICENSE content must preserve source URL and license context.
- User notes and search history may contain sensitive personal research and should remain private.
- Matrix bot credentials and crypto state must be protected and backed up.
