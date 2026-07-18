# Streaming Digest Data Model

Status: MVP conceptual/logical data model
Database: PostgreSQL + pgvector

## 1. Design principles

- Preserve original scraped values.
- Store user overrides separately and use effective values for search/embedding.
- Keep immutable-ish source identity fields stable.
- Model search around video cluster search results with parent video context.
- Treat ingestion as resumable and retryable.
- Store embeddings with source content hashes and model metadata.
- Store screenshots/files on mounted volume, not in PostgreSQL.
- Store domain events and warning/error summaries in PostgreSQL, but keep full logs in Loki.
- Split canonical external resources from per-video link occurrences.
- Represent long-running operations explicitly so API/UI callers track stable application operations instead of Hangfire internals.
- Keep onboarding/readiness, upgrade/version, backup, and rate-limit deferment state structured enough for Admin UI workflows.

## 2. Core identity rules

- Channel unique identity: YouTube channel ID.
- Video unique identity: composite `(platform, platform_video_url, platform_video_id)`. For YouTube, `platform_video_url` is the normalized watch URL without query string and `platform_video_id` is the YouTube video ID.
- Repository unique identity: canonical normalized repository URL. MVP supports GitHub. GitLab and Bitbucket are MVP+.
- External resource unique identity: canonical URL after safe redirect resolution and tracking-parameter removal.
- External link occurrence unique identity: video + source type + normalized URL occurrence.
- Segment unique identity: video ID + segment generation + sequence, with source type and timestamp retained for diagnostics/search links.
- Search document unique identity: source entity + source field/chunk + content hash.
- Embedding unique identity: search document/query + embedding provider + model + dimensions + content hash.

## 2.1 Value conventions

Editable scraped fields use the recommended compromise:

- Keep explicit `*_original` and `*_override` columns on core scraped entities.
- Application logic exposes `effective = override if override is not null else original`.
- Search documents and embeddings are built from effective values.
- Preserve original/source display so users can compare scraped and corrected values.
- User-authored notes are not scraped data and therefore store `markdown` directly rather than `markdown_original`/`markdown_override`.
- Override changes are recorded in `field_override_history`.

API DTOs for editable scraped fields should expose original, override, and effective values.

## 3. Tables

### 3.1 `app_users`

Single-user auth table.

Columns:

- `id uuid primary key`
- `username text not null unique`
- `password_hash text not null`
- `password_hash_algorithm text not null default 'argon2id'`
- `must_change_password boolean not null default true`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`
- `last_login_at timestamptz null`

### 3.2 `app_settings`

Database-backed user/admin settings.

Columns:

- `key text primary key`
- `value_json jsonb not null`
- `updated_at timestamptz not null`

Important settings:

- `ingestion.defaultMaxAgeDays`, default `30`
- `ingestion.defaultConcurrency`, default `1` or `2`
- `ingestion.maxSegmentsPerVideo`, default `60`
- `ingestion.defaultScheduleLocalTime`, default `06:00`
- `ingestion.tempMedia.maxBytes`, default to 50% of first-run free disk bytes
- `screenshots.offsetSeconds`, default `5`
- `search.textWeight`
- `search.vectorWeight`
- `search.highSignalThresholdPercent`, default `80`
- `search.recentSearchRetentionDays` if later added; MVP supports clear-all rather than granular deletion
- `notifications.matrix.enabled`
- `notifications.matrix.onManualRuns`
- `notifications.matrix.onScheduledRuns`
- `observability.enabled`
- `observability.links.grafanaUrl`
- `observability.links.hangfireUrl`
- `debug.rawHtmlCapture.enabledDefault`

### 3.3 `app_readiness_checks`

Structured first-run/onboarding/readiness state.

Columns:

- `id uuid primary key`
- `check_key text not null unique`
- `status text not null`
- `last_checked_at timestamptz null`
- `last_success_at timestamptz null`
- `last_error_summary text null`
- `details_json jsonb null`
- `required_for_core_setup boolean not null default false`
- `required_for_full_readiness boolean not null default false`
- `updated_at timestamptz not null`

Suggested `check_key` values: `admin_password_changed`, `embedding_model_verified`, `llm_model_verified`, `audio_to_text_verified`, `matrix_verified`, `observability_verified`, `first_channel_added`, `schedule_confirmed`, `backup_path_verified`.

### 3.4 `app_versions`

Structured version compatibility state for diagnostics, startup checks, and Upgrade & Maintenance UI.

Columns:

- `id uuid primary key`
- `app_version text not null`
- `db_schema_version text not null`
- `config_schema_version text not null`
- `deployment_schema_version text not null`
- `recorded_at timestamptz not null`
- `details_json jsonb null`

The latest row represents current recorded compatibility. EF migrations remain the source of truth for detailed DB migration history.

### 3.5 `channels`

Configured YouTube channels.

Columns:

- `id uuid primary key`
- `youtube_channel_id text not null unique`
- `name_original text not null`
- `name_override text null`
- `profile_url text not null`
- `source_url text not null`
- `description_original text null`
- `description_override text null`
- `is_paused boolean not null default false`
- `default_max_age_days integer null`
- `default_backfill_max_videos integer null`
- `last_ingested_at timestamptz null`
- `last_ingestion_status text null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Effective name/description are override if present, else original.

### 3.6 `videos`

YouTube videos.

Columns:

- `id uuid primary key`
- `platform text not null default 'youtube'`
- `platform_video_url text not null`
- `platform_video_id text not null`
- `youtube_video_id text not null` retained as YouTube convenience alias to `platform_video_id`
- `channel_id uuid not null references channels(id)`
- `author_original text not null`
- `author_override text null`
- `title_original text not null`
- `title_override text null`
- `description_original text null`
- `description_override text null`
- `video_url text not null`
- `published_at timestamptz null`
- `duration_seconds integer null`
- `thumbnail_url text null`
- `is_long_form boolean not null default true`
- `ingestion_status text not null`
- `transcript_status text not null default 'unknown'`
- `screenshot_status text not null default 'unknown'`
- `processing_version text null`
- `last_successful_ingestion_run_id uuid null references ingestion_runs(id)`
- `last_failed_ingestion_run_id uuid null references ingestion_runs(id)`
- `metadata_fetched_at timestamptz null`
- `transcript_fetched_at timestamptz null`
- `links_extracted_at timestamptz null`
- `search_indexed_at timestamptz null`
- `raw_metadata_json jsonb null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Indexes:

- Unique `(platform, platform_video_url, platform_video_id)`.
- Index `(platform, platform_video_id)`.

Suggested statuses:

- `pending`
- `processing`
- `processed`
- `processed_with_warnings`
- `failed`
- `skipped`

### 3.7 `video_transcripts`

Transcript source records.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `source_type text not null`
- `language_code text null`
- `is_auto_generated boolean null`
- `is_active boolean not null default true`
- `model_name text null`
- `engine_name text null`
- `full_text_original text not null`
- `full_text_override text null`
- `confidence numeric null`
- `created_at timestamptz not null`

`source_type` examples:

- `youtube_caption`
- `youtube_auto_caption`
- `local_whisper`

### 3.8 `transcript_cues`

Timestamped transcript cues.

Columns:

- `id uuid primary key`
- `transcript_id uuid not null references video_transcripts(id)`
- `sequence integer not null`
- `start_seconds numeric not null`
- `end_seconds numeric null`
- `text_original text not null`
- `text_override text null`
- `created_at timestamptz not null`

Index:

- `(transcript_id, sequence)` unique.

### 3.9 `segment_generations`

A segment generation groups one active or candidate set of segments for a video. This keeps normal daily ingestion stable and makes explicit segment regeneration auditable.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `source_type text not null`
- `generation_version integer not null`
- `is_active boolean not null default false`
- `requires_user_approval boolean not null default false`
- `status text not null`
- `llm_model text null`
- `llm_prompt_version text null`
- `created_by_operation_id uuid null references operations(id)`
- `created_at timestamptz not null`
- `activated_at timestamptz null`

Indexes:

- Unique `(video_id, generation_version)`.
- At most one active generation per video should be enforced by a partial unique index where practical.

### 3.10 `segments`

Video chapters or semantic segments.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `segment_generation_id uuid not null references segment_generations(id)`
- `source_type text not null`
- `sequence integer not null`
- `start_seconds numeric not null`
- `end_seconds numeric null`
- `title_original text not null`
- `title_override text null`
- `summary_original text null`
- `summary_override text null`
- `llm_model text null`
- `llm_prompt_version text null`
- `is_active boolean not null default true`
- `requires_embedding_approval boolean not null default false`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`source_type` examples:

- `author_chapter`
- `semantic_llm`
- `deterministic_chunk`

Indexes:

- Unique `(segment_generation_id, sequence)`.
- Index `(video_id, start_seconds)`.
- Index `(video_id, is_active)`.

### 3.11 `segment_transcript_ranges`

Mapping between segments and transcript cues/chunks.

Columns:

- `segment_id uuid not null references segments(id)`
- `transcript_cue_id uuid not null references transcript_cues(id)`
- primary key `(segment_id, transcript_cue_id)`

### 3.12 `screenshots`

Screenshot metadata.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `segment_id uuid null references segments(id)`
- `timestamp_seconds numeric not null`
- `file_path text not null`
- `storage_key text null`
- `public_url_path text null`
- `mime_type text not null default 'image/webp'`
- `width integer null`
- `height integer null`
- `file_size_bytes bigint null`
- `content_hash text null`
- `created_at timestamptz not null`

### 3.13 `external_resources`

Canonical external resources after safe URL normalization/redirect handling. A single resource may be referenced by many videos.

Columns:

- `id uuid primary key`
- `canonical_url text not null unique`
- `final_url text null`
- `domain text null`
- `resource_type text not null default 'unknown'`
- `title_original text null`
- `title_override text null`
- `description_original text null`
- `description_override text null`
- `classification_original text not null default 'unknown'`
- `classification_override text null`
- `classification_confidence numeric null`
- `classification_method text null`
- `is_ad_or_sponsor boolean not null default false`
- `raw_metadata_json jsonb null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Classification values:

- `code_repository`
- `website_resource`
- `ad_sponsor`
- `affiliate`
- `social`
- `newsletter`
- `course`
- `merch`
- `unknown`
- `other`

`resource_type` examples: `repository`, `website`, `social`, `document`, `unknown`.

### 3.14 `external_link_occurrences`

Links found in video descriptions, pinned comments, and derived sources. This table models occurrence/context, not canonical resource identity.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `external_resource_id uuid null references external_resources(id)`
- `source_type text not null`
- `source_entity_type text null`
- `source_entity_id uuid null`
- `source_text text null`
- `original_url text not null`
- `normalized_url text not null`
- `final_url text null`
- `position integer null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`source_type` examples: `video_description`, `pinned_comment`, `transcript`, `manual_note`.

Indexes:

- Unique `(video_id, source_type, normalized_url)` initially.
- Index `(external_resource_id)`.

### 3.15 `repositories`

Canonical repository records.

Columns:

- `id uuid primary key`
- `host text not null`
- `canonical_url text not null unique`
- `owner text null`
- `name text null`
- `normalized_owner text null`
- `normalized_name text null`
- `default_branch text null`
- `description_original text null`
- `description_override text null`
- `stars integer null`
- `forks integer null`
- `primary_language text null`
- `topics text[] null`
- `license_spdx_id text null`
- `deepwiki_url text null`
- `deepwiki_checked_at timestamptz null`
- `raw_metadata_json jsonb null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`host` values:

- `github` for MVP.
- `gitlab` and `bitbucket` are MVP+.

### 3.16 `external_resource_repositories`

Many-to-many link between canonical external resources and repositories.

Columns:

- `external_resource_id uuid not null references external_resources(id)`
- `repository_id uuid not null references repositories(id)`
- primary key `(external_resource_id, repository_id)`

### 3.17 `repository_documents`

README/LICENSE documents.

Columns:

- `id uuid primary key`
- `repository_id uuid not null references repositories(id)`
- `document_type text not null`
- `path text null`
- `source_url text null`
- `content_original text not null`
- `content_override text null`
- `content_hash text not null`
- `etag text null`
- `last_modified text null`
- `fetch_status text not null default 'succeeded'`
- `error_summary text null`
- `fetched_at timestamptz not null`
- `created_at timestamptz not null`

`document_type` values:

- `readme`
- `license`

README chunks are embedded. LICENSE content is stored but not embedded by default.

### 3.18 `scraped_pages`

First-page website scraping results.

Columns:

- `id uuid primary key`
- `external_resource_id uuid not null references external_resources(id)`
- `final_url text not null`
- `title_original text null`
- `title_override text null`
- `description_original text null`
- `description_override text null`
- `opengraph_json jsonb null`
- `visible_text_original text null`
- `visible_text_override text null`
- `robots_allowed boolean null`
- `scrape_status text not null`
- `exclusion_reason text null`
- `http_status integer null`
- `content_type text null`
- `content_hash text null`
- `fetch_duration_ms integer null`
- `page_size_bytes bigint null`
- `scraped_at timestamptz null`
- `raw_html_debug_path text null`
- `error_summary text null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Raw HTML is not stored by default. If per-video debug capture is enabled, store raw HTML on a debug volume/file path and record path here. Rejected/excluded scrape attempts are persisted with `scrape_status = 'excluded'` and `exclusion_reason`; future retries skip that URL unless the URL value changes.

### 3.19 `notes`

Private user-authored notes. Notes are not scraped data and therefore do not use original/override fields.

Columns:

- `id uuid primary key`
- `target_type text not null`
- `target_id uuid not null`
- `title text null`
- `markdown text not null`
- `embedding_status text not null default 'stale'`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`
- `deleted_at timestamptz null`

`target_type` values:

- `video`
- `segment`
- `external_resource`
- `repository`

Notes are embedded using `markdown`.

### 3.20 `field_override_history`

Version history for user overrides.

Columns:

- `id uuid primary key`
- `entity_type text not null`
- `entity_id uuid not null`
- `field_name text not null`
- `previous_value text null`
- `new_value text null`
- `changed_at timestamptz not null`

Single-user MVP does not require changed_by, but schema may add it later.

### 3.21 `search_documents`

Searchable document abstraction. Fine-grained search documents remain the primary search units; video clusters are result aggregation units.

Columns:

- `id uuid primary key`
- `document_type text not null`
- `source_entity_type text not null`
- `source_entity_id uuid not null`
- `source_field_name text null`
- `chunk_index integer null`
- `chunk_start_offset integer null`
- `chunk_end_offset integer null`
- `token_count integer null`
- `search_weight numeric null`
- `embedding_required boolean not null default true`
- `parent_video_id uuid null references videos(id)`
- `parent_segment_id uuid null references segments(id)`
- `title_effective text null`
- `body_effective text null`
- `metadata_json jsonb null`
- `fts_weight_config jsonb null`
- `content_hash text not null`
- `is_stale boolean not null default false`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`document_type` examples:

- `video_metadata`
- `segment_title`
- `transcript_chunk`
- `external_resource_metadata`
- `scraped_page_text`
- `repository_readme_chunk`
- `note`

Indexes:

- Full-text GIN on weighted `title_effective` + `body_effective`.
- Trigram indexes on key text columns for partial matches.
- `(source_entity_type, source_entity_id)`.
- `(parent_video_id)`.
- `(document_type)`.
- Unique identity over `(source_entity_type, source_entity_id, source_field_name, chunk_index, content_hash)` where practical.

### 3.22 `embeddings`

Vector embeddings for search documents. All vector comparisons for a query must use the active provider/model/dimensions.

Columns:

- `id uuid primary key`
- `search_document_id uuid not null references search_documents(id)`
- `provider text not null`
- `model text not null`
- `dimensions integer not null`
- `content_hash text not null`
- `source_text_hash text null`
- `embedding vector not null`
- `embedding_status text not null default 'succeeded'`
- `error_summary text null`
- `generated_by_operation_id uuid null references operations(id)`
- `created_at timestamptz not null`

Indexes:

- HNSW or IVFFlat vector index depending pgvector version and dataset size.
- Unique `(search_document_id, provider, model, dimensions, content_hash)`.

### 3.23 `video_cluster_embeddings`

Required MVP aggregate vectors for video-cluster scoring, high-signal digest matching, and coarse related-item discovery. Do not use these as the only search index; fine-grained `search_documents` remain the primary search units.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `provider text not null`
- `model text not null`
- `dimensions integer not null`
- `content_hash text not null`
- `embedding vector not null`
- `component_weights_json jsonb not null`
- `is_stale boolean not null default false`
- `requires_user_approval boolean not null default false`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Construction:

- Build only from normalized embeddings that share provider/model/dimensions.
- Weighted centroid is acceptable for digest/high-signal matching when all vectors are in the same embedding space.
- Search ranking should still aggregate document scores rather than rely only on this centroid.

### 3.24 `recent_searches`

Search history used for recent-search UI and high-signal digest matching.

Columns:

- `id uuid primary key`
- `query_text text not null`
- `searched_at timestamptz not null`
- `text_weight numeric not null`
- `vector_weight numeric not null`
- `filters_json jsonb null`

MVP supports clear-all search history. Clearing recent search history deletes `recent_searches` rows only; associated interaction events and other historical effects are retained unless separately purged by future privacy tooling. Granular per-search deletion is MVP+.

### 3.25 `search_query_embeddings`

Embeddings for recent search queries. This table avoids a circular relation between `recent_searches` and generic `embeddings`.

Columns:

- `id uuid primary key`
- `recent_search_id uuid not null references recent_searches(id)`
- `provider text not null`
- `model text not null`
- `dimensions integer not null`
- `content_hash text not null`
- `embedding vector not null`
- `created_at timestamptz not null`

Indexes:

- Vector index using the same pgvector strategy as content embeddings.
- Unique `(recent_search_id, provider, model, dimensions, content_hash)`.

Mismatched provider/model/dimensions are ignored for active-vector comparisons and regenerated as needed.

### 3.26 `user_interaction_events`

MVP user signals for clicked/opened result boosts.

Columns:

- `id uuid primary key`
- `recent_search_id uuid null references recent_searches(id)`
- `video_id uuid null references videos(id)`
- `search_document_id uuid null references search_documents(id)`
- `result_type text not null`
- `event_type text not null`
- `activated_at timestamptz not null`
- `metadata_json jsonb null`

`event_type` MVP values:

- `result_opened`
- `timestamp_opened`
- `repository_opened`
- `website_opened`
- `note_opened`

### 3.27 `operations`

Application-owned long-running operation records. Operations provide stable API/UI status over Hangfire jobs, migrations, backups, retries, and derived-data regeneration.

Columns:

- `id uuid primary key`
- `operation_type text not null`
- `status text not null`
- `risk_level text null`
- `requested_by text null`
- `related_entity_type text null`
- `related_entity_id uuid null`
- `hangfire_job_id text null`
- `started_at timestamptz null`
- `completed_at timestamptz null`
- `summary_json jsonb null`
- `error_summary text null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`operation_type` examples: `ingestion_run`, `retry_stage`, `regenerate_embeddings`, `segment_regeneration`, `backup`, `migration`, `model_download`, `health_check`.

### 3.28 `ingestion_runs`

Top-level manual/scheduled/backfill runs.

Columns:

- `id uuid primary key`
- `operation_id uuid null references operations(id)`
- `correlation_id text null`
- `schedule_id text null`
- `run_type text not null`
- `triggered_by text not null`
- `requested_by_user_id uuid null references app_users(id)`
- `status text not null`
- `started_at timestamptz not null`
- `completed_at timestamptz null`
- `channels_checked integer not null default 0`
- `new_videos_found integer not null default 0`
- `videos_ingested integer not null default 0`
- `videos_failed integer not null default 0`
- `videos_skipped integer not null default 0`
- `transcripts_found integer not null default 0`
- `transcripts_missing integer not null default 0`
- `repositories_found integer not null default 0`
- `config_snapshot_json jsonb null`
- `summary_json jsonb null`
- `created_at timestamptz not null`

`run_type` values:

- `scheduled`
- `manual`
- `backfill`

### 3.29 `ingestion_items`

Per-channel/video/link/repo work item status.

Columns:

- `id uuid primary key`
- `ingestion_run_id uuid not null references ingestion_runs(id)`
- `operation_id uuid null references operations(id)`
- `item_type text not null`
- `item_id uuid null`
- `external_key text null`
- `idempotency_key text null`
- `depends_on_item_id uuid null references ingestion_items(id)`
- `stage text not null`
- `stage_version text null`
- `job_payload_version text null`
- `status text not null`
- `attempt integer not null default 0`
- `retry_count integer not null default 0`
- `max_attempts integer null`
- `is_retryable boolean not null default true`
- `next_retry_at timestamptz null`
- `deferred_until timestamptz null`
- `deferment_reason text null`
- `worker_id text null`
- `started_by_job_id text null`
- `completed_by_job_id text null`
- `error_summary text null`
- `started_at timestamptz null`
- `completed_at timestamptz null`
- `created_at timestamptz not null`

Retryable stage names include `metadata`, `transcript`, `audio_transcription`, `segmentation`, `screenshots`, `link_extraction`, `link_classification`, `repository_metadata`, `repository_readme`, `repository_license`, `deepwiki_check`, `website_scrape`, `search_documents`, `embeddings`, and `notification`.

### 3.30 `rate_limit_deferments`

Persistent rate-limit/deferment state for external dependencies.

Columns:

- `id uuid primary key`
- `scope_type text not null`
- `scope_key text not null`
- `reason text not null`
- `retry_after_at timestamptz not null`
- `status text not null`
- `details_json jsonb null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`scope_type` examples: `youtube`, `repository_host`, `website_host`, `deepwiki`.

`status` values: `active`, `expired`, `cleared`.

Repository/API rate limits defer remaining work instead of failing the entire run. Workers check active deferments before starting host-scoped work.

### 3.31 `domain_events`

Important application/domain events and warning/error summaries.

Columns:

- `id uuid primary key`
- `event_type text not null`
- `severity text not null`
- `entity_type text null`
- `entity_id uuid null`
- `ingestion_run_id uuid null references ingestion_runs(id)`
- `operation_id uuid null references operations(id)`
- `message text not null`
- `details_json jsonb null`
- `created_at timestamptz not null`

Retention default:

- Warning/error/domain summaries: 90 days unless configured.
- Ingestion run summaries: retained longer/indefinitely.

### 3.32 `classification_corrections`

User corrections for link classification learning.

Columns:

- `id uuid primary key`
- `external_resource_id uuid not null references external_resources(id)`
- `domain text null`
- `scope text not null default 'exact_url'`
- `pattern text null`
- `previous_classification text not null`
- `corrected_classification text not null`
- `correction_note text null`
- `is_active boolean not null default true`
- `created_at timestamptz not null`

`scope` values: `exact_url`, `domain`, `pattern`.

Used for deterministic domain allow/block/classification lists and few-shot examples in local LLM prompts.

### 3.33 `notifications`

Notification audit records. MVP sends Matrix notifications without requiring E2EE; E2EE fields become relevant in MVP+.

Columns:

- `id uuid primary key`
- `operation_id uuid null references operations(id)`
- `ingestion_run_id uuid null references ingestion_runs(id)`
- `notification_type text not null default 'ingestion_summary'`
- `provider text not null default 'matrix'`
- `target text not null`
- `status text not null`
- `payload_json jsonb null`
- `rendered_body text null`
- `message_summary text null`
- `provider_message_id text null`
- `attempt_count integer not null default 0`
- `next_retry_at timestamptz null`
- `error_summary text null`
- `sent_at timestamptz null`
- `created_at timestamptz not null`

MVP+ Matrix E2EE additions:

- `encrypted boolean not null default false`
- `matrix_device_id text null`
- `verification_status text null`
- Matrix crypto/session state is stored on a mounted volume, not in this table.

### 3.34 `outbox_messages`

Reliable application message/outbox records for dispatching Matrix notifications and other side effects.

Columns:

- `id uuid primary key`
- `message_type text not null`
- `aggregate_type text null`
- `aggregate_id uuid null`
- `payload_json jsonb not null`
- `status text not null`
- `attempt_count integer not null default 0`
- `next_attempt_at timestamptz null`
- `last_error_summary text null`
- `created_at timestamptz not null`
- `sent_at timestamptz null`

### 3.35 `backup_artifacts`

Backup records for the MVP backup button and Upgrade & Maintenance UI.

Columns:

- `id uuid primary key`
- `operation_id uuid null references operations(id)`
- `status text not null`
- `backup_type text not null`
- `path text not null`
- `size_bytes bigint null`
- `content_hash text null`
- `started_at timestamptz not null`
- `completed_at timestamptz null`
- `error_summary text null`
- `created_by text null`
- `metadata_json jsonb null`

`backup_type` values: `full`, `db_only`, `media_only`, `config_only`.

### 3.36 `maintenance_operations`

High-level maintenance operation records for backup, restore validation, migrations, index rebuilds, and derived-data regeneration.

Columns:

- `id uuid primary key`
- `operation_id uuid null references operations(id)`
- `operation_type text not null`
- `status text not null`
- `risk_level text null`
- `started_at timestamptz null`
- `completed_at timestamptz null`
- `summary_json jsonb null`
- `error_summary text null`
- `created_at timestamptz not null`

## 4. Effective values

For editable fields, application logic should expose effective values:

- `effective = override if override is not null else original`

Search documents and embeddings must use effective values.

Source/raw display should allow user to compare original and override.

## 5. Search document construction

Create/update search documents for:

- Video metadata: effective title + effective description.
- Segment title/summary: effective title + effective summary.
- Transcript chunks: effective transcript cue/chunk text.
- External resource metadata: effective title/description/classification/domain.
- Scraped page text: effective page title/description/visible text.
- Repository README chunks: effective README content.
- Notes: note markdown.

Do not embed LICENSE by default.

## 6. Hybrid search implementation notes

Text search:

- Use PostgreSQL full-text search for tokenized ranking.
- Use trigram similarity for partial matches.
- Weight title higher than body.

Vector search:

- Use pgvector cosine or inner-product distance depending model normalization.
- Store model/dimension metadata.
- Regenerate embeddings when model changes.

Hybrid ranking:

- Individual document score: `document_score = textWeight * normalizedTextScore + vectorWeight * normalizedVectorScore`.
- Top-level MVP result unit is always a video cluster. Filter matched source material with `matchedDocumentTypes`, not mixed top-level result types.
- UI label is `Relative similarity`; it is a normalized vector rank score within the pre-pagination candidate set, displayed as a percentage with a tooltip explaining that it is relative to the active query/model/result set and not an absolute semantic truth or confidence.
- Calculate `relativeSimilarityPercent` over the top candidate set before pagination, defaulting to the top 200 vector candidates before hybrid aggregation. If max and min vector score are equal, return `100` for matching candidates.
- Video cluster score is a weighted aggregate over the top document matches for one video cluster. MVP formula:
  - `base = 0.65 * max(document_score) + 0.25 * average(top 3 document_scores) + 0.10 * coverage_score`
  - `coverage_score = min(distinctMatchedDocumentTypes / 4, 1.0)`
  - `note_boost = 0.08` when the cluster has a matching note, otherwise `0`
  - `interaction_boost = min(0.05, 0.01 * recent_open_count_for_cluster)`
  - `cluster_score = min(1.0, base + note_boost + interaction_boost)`
- Return score components for explainability.
- Include matched field/snippet.

## 7. Staleness and invalidation rules

MVP invalidation should be narrow and explicit:

- Editing video top-level metadata marks the video metadata search document stale and marks the corresponding `video_cluster_embeddings` row stale. It does not automatically mark all segment, link, repository, or website documents stale.
- Editing transcript, segment, external resource, repository, or website fields marks only the directly affected search document(s) stale plus the parent video-cluster aggregate embedding.
- Creating, editing, or clearing a note updates the note embedding/search document and the parent video-cluster aggregate embedding so repeated searches reflect live state.
- Classification corrections mark relevant external resource documents stale and influence future classification through rules/few-shot examples.
- Related-item caches, if introduced later, must be invalidated when their source embeddings or cluster aggregates become stale.

Mutation APIs should return stale search-document IDs, stale cluster IDs, and queued operation/job IDs when relevant.

## 8. Retention

- Raw transcripts: retain indefinitely unless video/channel deleted.
- Screenshots: retain indefinitely unless purged/deleted.
- Ingestion run summaries: retain indefinitely or configurable long retention.
- Detailed ingestion events: 90 days by default, unless disk-based first-run policy lowers retention.
- Logs/metrics/traces: 90 days when first-run free space is greater than 5 GB; 30 days when greater than 1 GB; disabled with warning when 1 GB or less is available.
- Old embeddings: invalidated when the active embedding model changes after user confirmation, then regenerated in background.

## 9. Deletion behavior

Channel deletion options:

- Stop future ingestion only.
- Delete channel and all related videos, segments, transcripts, link occurrences, notes, search documents, embeddings, screenshots, and resource associations.

Canonical repositories and external resources may be shared by multiple videos/links. If deleting a channel, remove associations/occurrences and delete canonical resources only when no remaining associations exist, unless user requests force purge.

Screenshots and raw debug captures must be removed from file volumes when corresponding records are purged.

## 10. Configuration ownership

The configuration split is:

- Docker environment variables and Docker secrets: bootstrap credentials, secrets, service wiring, runtime environment, mounted volume paths.
- Schema-validated JSON config: durable runtime/deployment configuration and first-run outputs that must survive restarts.
- PostgreSQL app settings: user-facing product behavior, onboarding/readiness state, operational state, and domain data.

Recommended MVP config files:

- `config/streaming-digest.runtime.json`
- `config/streaming-digest.deployment.json`

Each config file should include an explicit schema version and be validated on startup. Config migration should preserve unknown keys when safe and report exact JSON paths for invalid values.

## 11. Migration notes

Use EF Core migrations for core schema and raw SQL for:

- Extensions.
- pgvector columns/indexes.
- full-text generated columns/indexes if used.
- trigram indexes.
- specialized search functions/views.

All migrations should be idempotent where practical for deployment reliability. Workers must not process jobs until DB/config/deployment compatibility checks pass.
