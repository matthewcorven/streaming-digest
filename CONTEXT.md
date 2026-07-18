# Streaming Digest

A self-hosted personal YouTube knowledge base: ingests long-form videos from configured channels, enriches them (transcripts, segments, screenshots, links, repos, notes, embeddings), and serves hybrid search clustered by video.

## Language

### Search & result health

**Video Health**:
The single derived, read-model badge shown for a video (e.g. the `processingStatus` on search result cards), computed as the worst of: ingestion status, any failed or deferred non-skippable stage, and any stale search document or cluster embedding. Precedence (worst first): `failed` > `deferred` > `stale` > `processed_with_warnings` > `processed`. Stage-level statuses remain for run-detail and diagnostics views; they never drive the top-level badge independently.
_Avoid_: processing state, status rollup, health score

### Segmentation

**Segment**:
A single timecoded piece of a video (chapter or semantic chunk), with a title, summary, and optional screenshot. Segments are immutable once written and belong to exactly one Segment Generation.
_Avoid_: chunk, chapter, scene

**Segment Generation**:
One complete, versioned set of Segments for a video. Exactly one generation is Active per video at a time. A new generation created by explicit user action starts inactive and may require approval before activation; generations are never deleted in MVP, preserving history.
_Avoid_: segment set, version, batch

**Active Generation**:
The single Segment Generation whose segments drive search, screenshots, embeddings, and UI display for its video. Approving a candidate generation atomically flips which generation is Active.
_Avoid_: current segments, live segments

**Orphaned Note**:
A Note anchored to a Segment that belongs to a non-Active generation after a re-segmentation approval. Orphaned Notes are preserved, hidden from normal display, surfaced in the pending-action inbox, and can be re-anchored (best-effort nearest timestamp) or deleted by the user.
_Avoid_: stale note, detached note

**Re-segmentation cutover**:
The approval of a new Segment Generation: the new generation becomes Active; the old generation's screenshots are purged, its search documents and embeddings are marked stale and regenerated for the new segments, and its Notes become Orphaned Notes.
_Avoid_: regeneration, reprocessing

### Staleness & invalidation

**Search Document**:
A searchable unit of text built from the Effective Value of a source entity (video metadata, segment, transcript chunk, note, repo README, scraped page, external resource). The primary unit of search and embedding.
_Avoid_: index entry, search record

**Stale (Search Document)**:
A derived condition, not stored state: a Search Document is stale when its stored content hash no longer matches the Effective Value of its source entity. There is one source of truth for staleness; it is never a separately-written flag that can drift out of sync.
_Avoid_: outdated, dirty, needs-reindex (as stored flags)

**Stale (Embedding / Cluster Embedding)**:
A derived condition: an embedding is stale when its parent Search Document is stale, or when the active embedding provider/model/dimensions differ from those the embedding was generated with. Embeddings never carry their own staleness flag; their status column only records job outcome (`succeeded` / `failed` / `pending`).
_Avoid_: outdated vector, model mismatch (as stored flags)

**Effective Value**:
The value used for display, search, and embedding: the user override when present, otherwise the original scraped value. Originals and overrides are stored separately; the Effective Value is computed.
_Avoid_: display value, current value

**Video Cluster**:
The top-level unit of a search result: a search-time aggregation of every Search Document whose `parent_video_id` is a given video, grouped and scored together (max/top-3/coverage formula, plus note and interaction boosts). A Video Cluster is not a stored entity — a video with no matching Search Documents produces no cluster in that result set. One video appears in at most one cluster per result page; a shared repository or website may appear in the matches of several clusters.
_Avoid_: result group, video result, bundle

**Duplicated Resource Document**:
The convention that a Search Document derived from a shared canonical resource (e.g. a repository README chunk linked from two videos) is stored once per referencing video, with identical content and content hash but a distinct `parent_video_id`. This keeps cluster membership and scoring uniform — every Search Document has exactly one parent video — at trivial storage cost.
_Avoid_: shared document, multi-parent document

**Related Item**:
An on-demand discovery feature on an expanded result card: the top N (default 5) other corpus items most similar to the current cluster fingerprint, each shown with a Relative similarity percentage. No threshold is applied; it answers "what else is like this?" and compares cluster fingerprints against content.
_Avoid_: similar item, suggested item

**High-Signal Match**:
A per-ingestion-run digest feature: newly ingested items whose cluster fingerprint exceeds the global similarity threshold (default 80%) against a Recent Search embedding. It is a subscription signal answering "what new content matches something I searched for?" — compared against recent-search query embeddings only, never arbitrary content. Clearing search history removes the subscription for future runs. Uses the same fingerprints and similarity scale as Related Items, so percentages are comparable across both surfaces.
_Avoid_: digest match, interesting item

**Recent Search**:
A stored user query with its embedding, text/vector weights, and filters. Powers the recent-searches panel, High-Signal Match subscriptions, and ranking personalization. MVP supports clear-all deletion only; per-query deletion is MVP+.
_Avoid_: search history entry, past query

**Relative similarity**:
The normalized percentage shown next to matches and Related Items, computed within the pre-pagination candidate set (default top 200 vector candidates) for the active query and model. It is a rank-relative score, not a confidence or absolute semantic truth, and the UI tooltip must say so.
_Avoid_: similarity score, confidence, relevance percent

### Re-running work

**Retry**:
Re-execute work that is in a failed or deferred state — failed stages, failed ingestion items, failed notifications. Retry never applies to succeeded work and is always idempotent. This is one of the only two user-facing re-run verbs.
_Avoid_: rerun, redo, replay (for failed work)

**Reprocess**:
Re-execute the full pipeline for an already-succeeded entity (a video, a repository, an external resource), explicitly bypassing the idempotency/skip guard. Reprocessing marks the entity's Search Documents stale by derivation, so embeddings regenerate as a downstream consequence — the user never requests embedding regeneration separately except in the bulk model-change flow. This is the second of the only two user-facing re-run verbs.
_Avoid_: re-ingest, refresh, redo (for succeeded work)

### Channel & dependency health

**Degraded (Channel)**:
A stored channel-level state entered after two consecutive runs fail at the adapter stage. A Degraded channel is skipped by scheduled ingestion, but each scheduled run performs a single lightweight probe (one metadata fetch); a successful probe clears Degraded and the channel rejoins the run, a failed probe leaves Degraded in place and increments the failure count. Degraded is orthogonal to Deferment: an active Deferment pauses the failure counter (failures during a deferment don't count — the channel never had a fair chance). The user can manually clear Degraded, which only resets the counter — the next run re-trips it if the problem persists.
_Avoid_: broken channel, unhealthy channel, circuit-broken (as a separate user-facing term)

**Deferment**:
A stored, time-bound, host-scoped pause on external work (YouTube, a repository host, DeepWiki, a website host) caused by a rate-limit response. Workers check active Deferments before starting host-scoped work; work resumes after `Retry-After` or a configured default delay. Deferments are surfaced on the dashboard, run details, and daily digest, and can be manually cleared by the operator.
_Avoid_: rate-limit state, cooldown, backoff

### Work tracking

**Ingestion Run**:
An immutable historical record of one scheduled, manual, or backfill sweep: counts, stage summary, and final status as of `completed_at`. Once completed it is never rewritten — it answers "what happened in that sweep." Run-detail pages derive a live rollup from the current state of its items rather than re-reading the frozen record.
_Avoid_: job, batch, execution

**Ingestion Item**:
A living row tracking one unit of work (channel/video/link/repo stage) belonging to exactly one Ingestion Run. Retries mutate its status, attempt count, and timestamps in place; each retry also writes a domain event so the run-detail timeline shows the full history. An item stays attached to its originating run forever.
_Avoid_: task, job item, work unit

**Operation**:
The stable application-owned tracking handle for one user- or API-requested piece of long-running work (ingestion run, retry, reprocess, backup, migration, model download). One Operation can span many Ingestion Items (e.g. a batch retry); each item links only its latest Operation. Callers poll Operations, never Hangfire internals.
_Avoid_: job handle, task id, request ticket

### Links & resources

**Link**:
A single occurrence of a URL found in a specific video's description, pinned comment, or other source, with its position and context. Links are what extraction counts and what run-detail pages list; a Link whose canonical target is still being resolved shows a "resolving" badge. Rule of thumb: anything the user might legitimately see twice (once per video) is a Link.
_Avoid_: extracted URL, URL occurrence

**Resource**:
The canonical, deduplicated external thing one or more Links point at, after tracking-parameter removal and safe redirect resolution. Classification, scraping, repository association, and per-resource pages all operate on Resources. Digest counts ("2 new repositories, 3 new websites") count new Resources; run-detail extraction counts count new Links — the numbers may legitimately differ.
_Avoid_: canonical URL, external entity, website (as a stored concept)

**Core Stage**:
An ingestion stage whose failure makes a video fundamentally undeliverable for search: metadata, transcript (captions or Whisper fallback — success of either path), search documents, embeddings. A failed Core Stage sets the video's ingestion status to `failed` — meaning "not meaningfully searchable" — and is always retryable.
_Avoid_: required stage, blocking stage

**Enrichment Stage**:
An ingestion stage whose failure degrades but does not block search: segmentation, screenshots, link extraction, classification, repository documents, DeepWiki check, website scrape, pinned comment. A failed Enrichment Stage produces a warning; if all Core Stages succeeded, the video is `processed_with_warnings` and stays fully visible in search with a warning badge (e.g. "no transcript").
_Avoid_: optional stage, best-effort stage

**Author**:
The human-readable attribution text stored on a video (`author_original`/`author_override`), defaulting to the channel's effective name when the platform reports nothing distinct. Display-only metadata: included in the video-metadata Search Document so searching a guest or collab name can find the video, editable via override, but never an identity — Channel is the only organizational identity, and Author plays no role in filtering or relationships.
_Avoid_: creator, uploader, owner

**Digest**:
A stored, run-scoped artifact assembled once when an Ingestion Run completes: new videos, new Resources (repositories, websites), High-Signal Matches, failed/skipped items, and active Deferments, linked to its run. The dashboard renders the most recent Digest (with a hint when a newer run is in progress); the Matrix notification is an excerpt of the same stored Digest — one assembly, two renderings, never independently computed. Rolling windows and "since you last looked" views are MVP+.
_Avoid_: daily summary, report, notification payload

**Override**:
The single user-edit primitive for any scraped field (title, description, author, cue text, link metadata, classification, repository metadata). Stored alongside the preserved original; the Effective Value uses it when present; every change is recorded in override history with previous value and timestamp. Setting an Override to null retracts it.
_Avoid_: manual edit, custom value, user fix

**Correction**:
An Override applied specifically to a classification field — not a separate user action. Its side effect is appending a learning example to the classifier's few-shot/rule source. Retracting the classification Override deactivates the corresponding example, since the judgment it was based on has been withdrawn. Corrections are never edited directly, only created or retracted through classification Overrides.
_Avoid_: training edit, classifier feedback, reclassification (as a user verb)

**Notification**:
The single user-visible record of an outbound message attempt (e.g. a Matrix Digest send): provider, target, status, rendered body, provider message ID, attempt count, and error summary. Status is the record of truth; Retry always targets the Notification. Visible in ingestion-run details and admin UI.
_Avoid_: alert, message, ping

**Outbox Message**:
Internal delivery plumbing guaranteeing at-least-once dispatch of a Notification. Never surfaced in UI or API. Delivery outcome writes the Notification's status (`pending` → `sent`/`failed`), so the two can never disagree; a Notification retry enqueues a fresh Outbox Message.
_Avoid_: (not user-facing — do not use in copy)

**Onboarding**:
The one-time, wizard-style first-run flow ending at core-setup completion (password changed, embedding and LLM models verified, first channel added, schedule confirmed). Once complete it never re-opens — `isCoreSetupComplete` is a permanent fact, and no later failure routes the user back into it.
_Avoid_: setup wizard, first-run flow (as a re-openable thing)

**Readiness**:
The standing health surface backed by structured checks (embedding model, LLM, Whisper, Matrix, observability, backup path), re-verified on demand, on a daily schedule, and at startup. A previously-passing check that now fails surfaces as a pending-action inbox item and dashboard warning — never as Onboarding. Full Readiness is a moment-in-time property ("all green right now"), not a gate.
_Avoid_: health checks, setup status, verification state

**Active Embedding Model**:
The single global pointer (provider + model + dimensions) that all new embeddings and query embeddings use. Changing it requires explicit user confirmation, flips the pointer immediately, and queues a bulk Reprocess of all embeddings. There is never more than one active model, and vector comparisons only ever span a single model's space.
_Avoid_: current model, default model

**Embedding Transition**:
The declared state between flipping the Active Embedding Model and completion of the bulk regeneration Operation. During transition: new ingestion and queries embed with the new model; vector search covers only new-model embeddings while text search is unaffected; the UI shows a "search coverage rebuilding" banner with progress; High-Signal Match evaluation is skipped for runs completed mid-transition. The state is derived — active model differs from the model of the completed embedding generation — not stored separately.
_Avoid_: reindexing, migration mode, dual-model window

**Screenshot**:
A best-effort WebP visual artifact, one per Segment, stored on a mounted volume with metadata in the database. Never load-bearing: the serving endpoint returns a placeholder (segment title + timestamp) instead of a 404 when the file is missing, so cards keep a stable layout. A DB row whose file is missing at serve time is marked failed (retryable, since screenshots are an Enrichment Stage) and logged as a domain event, but never affects Video Health beyond the enrichment warning it already carries. A video's rolled-up screenshot status is `unknown` / `pending` / `partial` (at least one segment missing its shot) / `succeeded` / `failed`.
_Avoid_: thumbnail (that's the platform-provided image), frame grab, preview

**Temp Media**:
Stage-scoped scratch files (audio/video downloads for Whisper fallback) living under a `temp/` folder with content-addressed names (`{runId}/{videoId}/{stage}-{attempt}-{contentHashPrefix}.{ext}`). Re-running a download for identical content reuses the existing file; transcription results commit to the database before Temp Media is deleted, so a mid-stage crash is harmless. Quota is enforced before download starts — exceeding it defers the item rather than failing mid-write. Anything surviving past the next startup cleanup is definitionally orphaned and removed.
_Avoid_: scratch file, working file, cache

**Pending-Action Inbox**:
The dashboard's third-priority section: a computed projection over live state, with no stored rows and no read/unread tracking. An item appears while its underlying condition holds (failed ingestion, Degraded channel, active Deferment, stale embeddings, service warnings, storage warnings) and disappears the moment the condition resolves, whether or not the user ever saw it. Conditions requiring an explicit user decision — Orphaned Notes, pending Segment Generation approvals — persist until the user takes a real action (re-anchor/delete, approve/reject); there is no "dismiss" in MVP. Item ordering follows the product rule: pending approvals, failed ingestion, degraded channels, deferred rate limits, stale embeddings, model/service warnings, new digest items, recent-search matches, storage/retention warnings.
_Avoid_: notification center, alerts list, task queue

**Interaction Boost**:
The ranking lift a Video Cluster gets from the user's open/click signals: `min(0.05, 0.01 × event count)` where events are any interaction type (result, timestamp, repository, website, or note opened) within a rolling window (default 90 days, configurable via `search.interactionBoostWindowDays`), counted per cluster and capped at five. No decay curve inside the window — it is a current-interest signal, not a fossil record. Interaction events are retained indefinitely for audit; only the trailing window feeds ranking.
_Avoid_: click boost, engagement score, popularity

**Backfill**:
An Ingestion Run type that overrides selection rules, not processing rules: it selects videos by its own days/max-count window instead of the default max-age lookback, while the idempotency guard still applies — already-processed videos are skipped (Backfill is never an implicit Reprocess). Backfill runs produce a Digest marked with `run_type: backfill`; Matrix notification for Backfill runs defaults to off (`notifications.matrix.onBackfillRuns`, default `false`), since the user who triggered it is already watching.
_Avoid_: historical import, catch-up run, bulk ingest

**Long-form**:
The selection rule for ingestible videos: not classified by the platform as a Short (`/shorts/` URL form or Shorts metadata flag from the adapter) and duration at or above a configurable floor (`ingestion.minDurationSeconds`, default 61). Excluded videos produce no ingestion items; the run summary counts them as `videos_skipped` and the run detail lists them with reason `short_form`. A selection rule only — never retryable, never a failure.
_Avoid_: full-length, regular video

**Ingestion Schedule**:
The daily run trigger defined by two settings: `ingestion.scheduleLocalTime` (default `06:00`) and `ingestion.scheduleTimeZone` (IANA name, captured from the browser during onboarding, editable in settings). The run fires at that wall-clock time in the configured zone — shifting automatically across DST — never at a fixed UTC offset and never by the container's timezone. A missed fire (container down) does not catch up; the next fire is the next day, and a manual run is always available.
_Avoid_: cron, daily job, timer

**Paused (Channel)**:
A user-set ingestion gate: a Paused channel gets no selection, no probing, no processing, and no failure counting, while all its existing data stays fully searchable with no visual penalty. Channel state precedence is Deleted > Paused > Degraded > Active, each layer strictly narrowing what a run may do — Degraded probes only fire for non-Paused channels, and a Paused-Degraded channel stays Degraded until unpaused, when the next run's probe evaluates it fresh. Search never discriminates by channel state.
_Avoid_: disabled, muted, inactive

**Note**:
A single private markdown text attached to one target (video, segment, Resource, or repository). One Note per target in MVP — the modal edits "the note" for that target; multiple notes per target are MVP+. The primary affordance is the search-result card; the video detail surface also exposes the note action so any ingested video is notetable even if it has never matched a query. A Note's content is embedded, contributes to ranking, and its presence applies the note boost to its parent cluster; deleting it updates the parent aggregate.
_Avoid_: comment, annotation, memo (multiple notes per target)

**Repository**:
The enriched, host-API-backed entity for a code repository: metadata (owner, name, description, stars, language, topics, license), README and LICENSE documents, and DeepWiki status. It is the single source for repository metadata and overrides, and feeds repository search documents. When a Resource is classified `code_repository` and linked to a Repository, the Resource's title/description Effective Values delegate to the Repository — the Resource keeps only classification and scrape status, so the two can never disagree. The association is created eagerly at classification; the Repository row materializes when the metadata stage first succeeds. Result cards render Repository data whenever the link exists, Resource data only when it doesn't (with a "metadata unavailable" warning).
_Avoid_: repo record, project (as a stored entity)

**Transcript**:
A timestamped text record of a video's speech from one source (author-uploaded captions, platform auto-captions, or local Whisper). A video may hold several Transcripts; exactly one is Active. Source preference is fixed and automatic: `youtube_caption` > `local_whisper` > `youtube_auto_caption`. Manual Transcript selection is MVP+.
_Avoid_: captions, subtitles, transcription (as multiple active)

**Active Transcript**:
The single Transcript whose cues drive search documents, segment mapping, and display for its video. When a Reprocess discovers a higher-preference Transcript, activation switches automatically.
_Avoid_: current transcript, primary captions

**Transcript cutover**:
The activation of a higher-preference Transcript: cue-level Search Documents are marked stale and rebuilt from the new cues, and Segments re-map to the new cues by timestamp overlap within the same Segment Generation (boundaries don't change, so no new generation). Cue Overrides on the old Transcript stay attached to it — preserved but inert.
_Avoid_: transcript replacement, re-transcription

**Video**:
A long-form video ingested from a configured Channel — the central entity that Segments, Transcripts, Links, Screenshots, Notes, and Search Documents attach to. Video rows are created only by ingestion, never by link extraction: a YouTube URL found in a description is a plain Resource, and video-to-video linking is MVP+. An ingested Video whose platform source is later deleted keeps all its data unchanged; a Reprocess that gets a definitive "unavailable" response marks it terminally `unavailable` (metadata retries stop, watch links become best-effort) with no Video Health penalty beyond that flag.
_Avoid_: entry, item, content

**Search Launchpad**:
The dashboard's second-priority section (between Digest and Pending-Action Inbox) with exactly three components: the natural-language query box, the recent-searches panel (each entry re-runs its query with stored weights and filters), and compact High-Signal Match cards from the latest Digest deep-linking to result artifacts. The "resume your research" surface. Saved searches, filter presets, and trending views are MVP+.
_Avoid_: search home, quick search, discovery panel

**Channel**:
A configured YouTube content source: platform identity (channel ID, profile URL), scraped metadata (name, description), and ingestion configuration (paused, max-age, backfill bounds). Platform metadata refreshes automatically as a side effect of each run's channel resolution — including the Degraded probe — while Overrides stay untouched; there is no standalone channel-metadata stage. A channel rename on YouTube therefore flows through on the next run, and display continuity is the user's choice via Overrides.
_Avoid_: source, subscription, feed

**Embedding Model**:
The model that defines the vector space all embeddings and queries live in (provider + model ID + dimensions). Changing it is change-ceremonial: explicit confirmation, immediate pointer flip, Embedding Transition, bulk Reprocess of all embeddings. UI copy never says "change model" bare — always "change embedding model."
_Avoid_: model (bare), vector model

**LLM Model**:
The local instruction model used for link classification and segment refinement. Changing it is a plain settings save: it applies to future work only, invalidates nothing, and requires no confirmation ceremony — existing segments and classifications are not recomputed.
_Avoid_: model (bare), chat model

**Audio Model**:
The engine + model pair (e.g. whisper.cpp + base.en) used for the no-captions transcription fallback. Same free-change semantics as LLM Model: future transcriptions only, no invalidation, no ceremony.
_Avoid_: model (bare), speech model, whisper (as the model itself — whisper.cpp is the engine)

**DeepWiki Check**:
The per-Repository probe of `https://deepwiki.com/{owner}/{repo}`: the URL is stored only when the page is reachable and not the "Index your code" placeholder. DeepWiki is a host scope like any other — a 429 defers all remaining checks in the run rather than failing them. The outcome is write-once, except that negative outcomes (no page / placeholder) are re-checked on Repository Reprocess, since the repo may have been indexed since; a stored reachable URL is never re-verified in MVP.
_Avoid_: deepwiki validation, docs check

**Backup**:
One timestamped, content-hashed artifact directory (manifest + database dump + volume archive + config archive) recorded with the schema versions it was taken under. Restores are typed: a full Backup restores database and volumes together; a `db_only` restore applies the database alone and lets the existing missing-file machinery downgrade absent screenshots to retryable failures, so restore validation passes on login/search/embedding health. Backup verification checks artifact integrity and version compatibility, never live-volume consistency. Scheduled backups are MVP+.
_Avoid_: snapshot, export, dump

**Maintenance Operation**:
Not a separate kind of thing: an Operation whose type belongs to the maintenance family (`backup`, `migration`, `derived_data_regeneration`, `screenshot_purge`, `restore_validation`). The API and UI only ever talk about Operations; the Upgrade & Maintenance panel reads Operations filtered to this family. The separate `maintenance_operations` table is an invisible implementation detail adding maintenance-specific columns to the 1:1-linked Operation, and may merge into `operations.summary_json` with no user-visible change.
_Avoid_: maintenance task, upgrade job, admin operation

**Retry Budget**:
The bounded retry allowance for a failed item-stage: up to 2 automatic backoff attempts, then up to 5 user-triggered manual Retries. Reaching the cap marks the item permanently failed (`is_retryable = false`) with an explanation in the UI; the only paths forward are Reprocess — a deliberate fresh start that resets the budget — or exclusion rules that skip the URL. This bounds host hammering and keeps the inbox from becoming a retry slot machine.
_Avoid_: retry limit, attempt cap, max retries

**Recall Harness**:
The regression gate for the killer journey: a golden dataset of at least 20 vague natural-language queries, each written query-first — before its fixture video's metadata is finalized — so queries can't be reverse-engineered from the text. The gate is 100% top-3 recall on the dataset, no partial pass; any regression is fixed in ranking, weights, or document construction, never by editing the dataset to fit. The dataset grows whenever a real user query fails. It runs on every ranking-formula, Embedding Model, or document-construction change. It is a regression floor — "queries like these stay top-3" — not a claim about all possible vague queries.
_Avoid_: search tests, golden queries, eval set
