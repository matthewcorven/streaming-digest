# Streaming Digest Data Model

Status: MVP conceptual/logical data model
Database: PostgreSQL + pgvector

## 1. Design principles

- Preserve original scraped values.
- Store user overrides separately and use effective values for search/embedding.
- Keep immutable-ish source identity fields stable.
- Model search around best-match items with parent video context.
- Treat ingestion as resumable and retryable.
- Store embeddings with source content hashes and model metadata.
- Store screenshots/files on mounted volume, not in PostgreSQL.
- Store domain events and warning/error summaries in PostgreSQL, but keep full logs in Loki.

## 2. Core identity rules

- Channel unique identity: YouTube channel ID.
- Video unique identity: YouTube video ID.
- Repository unique identity: canonical normalized repository URL.
- External link unique identity: normalized final URL after redirects and tracking parameter removal.
- Segment unique identity: video ID + source type + start timestamp + sequence/version.
- Embedding unique identity: source entity + source field/chunk + embedding model + content hash.

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
- `screenshots.offsetSeconds`, default `5`
- `search.textWeight`
- `search.vectorWeight`
- `notifications.matrix.enabled`
- `notifications.matrix.onManualRuns`
- `notifications.matrix.onScheduledRuns`
- `observability.links.grafanaUrl`
- `observability.links.hangfireUrl`
- `debug.rawHtmlCapture.enabledDefault`

### 3.3 `channels`

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

### 3.4 `videos`

YouTube videos.

Columns:

- `id uuid primary key`
- `youtube_video_id text not null unique`
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
- `raw_metadata_json jsonb null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Suggested statuses:

- `pending`
- `processing`
- `processed`
- `processed_with_warnings`
- `failed`
- `skipped`

### 3.5 `video_transcripts`

Transcript source records.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `source_type text not null`
- `language_code text null`
- `is_auto_generated boolean null`
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

### 3.6 `transcript_cues`

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

### 3.7 `segments`

Video chapters or semantic segments.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
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
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`source_type` examples:

- `author_chapter`
- `semantic_llm`
- `deterministic_chunk`

Indexes:

- `(video_id, sequence)` unique.
- `(video_id, start_seconds)`.

### 3.8 `segment_transcript_ranges`

Mapping between segments and transcript cues/chunks.

Columns:

- `segment_id uuid not null references segments(id)`
- `transcript_cue_id uuid not null references transcript_cues(id)`
- primary key `(segment_id, transcript_cue_id)`

### 3.9 `screenshots`

Screenshot metadata.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `segment_id uuid null references segments(id)`
- `timestamp_seconds numeric not null`
- `file_path text not null`
- `mime_type text not null default 'image/webp'`
- `width integer null`
- `height integer null`
- `file_size_bytes bigint null`
- `content_hash text null`
- `created_at timestamptz not null`

### 3.10 `external_links`

Links found in descriptions/pinned comments and derived sources.

Columns:

- `id uuid primary key`
- `video_id uuid not null references videos(id)`
- `source_type text not null`
- `source_text text null`
- `original_url text not null`
- `normalized_url text not null`
- `final_url text null`
- `domain text null`
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

`source_type` examples:

- `video_description`
- `pinned_comment`

Index:

- unique `(video_id, normalized_url, source_type)` initially; consider separate canonical link table later.

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

### 3.11 `repositories`

Canonical repository records.

Columns:

- `id uuid primary key`
- `host text not null`
- `canonical_url text not null unique`
- `owner text null`
- `name text null`
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

- `github`
- `gitlab`
- `bitbucket`

### 3.12 `external_link_repositories`

Many-to-many link between links and repositories.

Columns:

- `external_link_id uuid not null references external_links(id)`
- `repository_id uuid not null references repositories(id)`
- primary key `(external_link_id, repository_id)`

### 3.13 `repository_documents`

README/LICENSE documents.

Columns:

- `id uuid primary key`
- `repository_id uuid not null references repositories(id)`
- `document_type text not null`
- `path text null`
- `content_original text not null`
- `content_override text null`
- `content_hash text not null`
- `fetched_at timestamptz not null`
- `created_at timestamptz not null`

`document_type` values:

- `readme`
- `license`

README chunks are embedded. LICENSE content is stored but not embedded by default.

### 3.14 `scraped_pages`

First-page website scraping results.

Columns:

- `id uuid primary key`
- `external_link_id uuid not null references external_links(id)`
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
- `scraped_at timestamptz null`
- `raw_html_debug_path text null`
- `error_summary text null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

Raw HTML is not stored by default. If per-video debug capture is enabled, store raw HTML on a debug volume/file path and record path here.

### 3.15 `notes`

Private user notes.

Columns:

- `id uuid primary key`
- `target_type text not null`
- `target_id uuid not null`
- `markdown_original text not null`
- `markdown_override text null`
- `title text null`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`target_type` values:

- `video`
- `segment`
- `external_link`
- `repository`

Notes are embedded using effective markdown.

### 3.16 `field_override_history`

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

### 3.17 `search_documents`

Searchable document abstraction.

Columns:

- `id uuid primary key`
- `document_type text not null`
- `source_entity_type text not null`
- `source_entity_id uuid not null`
- `parent_video_id uuid null references videos(id)`
- `parent_segment_id uuid null references segments(id)`
- `title_effective text null`
- `body_effective text null`
- `metadata_json jsonb null`
- `content_hash text not null`
- `is_stale boolean not null default false`
- `created_at timestamptz not null`
- `updated_at timestamptz not null`

`document_type` examples:

- `video_metadata`
- `segment_title`
- `transcript_chunk`
- `external_link_metadata`
- `scraped_page_text`
- `repository_readme_chunk`
- `note`

Indexes:

- Full-text GIN on weighted `title_effective` + `body_effective`.
- Trigram indexes on key text columns for partial matches.
- `(source_entity_type, source_entity_id)`.
- `(parent_video_id)`.
- `(document_type)`.

### 3.18 `embeddings`

Vector embeddings.

Columns:

- `id uuid primary key`
- `search_document_id uuid not null references search_documents(id)`
- `provider text not null`
- `model text not null`
- `dimensions integer not null`
- `content_hash text not null`
- `embedding vector not null`
- `created_at timestamptz not null`

Indexes:

- HNSW or IVFFlat vector index depending pgvector version and dataset size.
- Unique `(search_document_id, provider, model, content_hash)`.

### 3.19 `ingestion_runs`

Top-level manual/scheduled/backfill runs.

Columns:

- `id uuid primary key`
- `run_type text not null`
- `triggered_by text not null`
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
- `summary_json jsonb null`
- `created_at timestamptz not null`

`run_type` values:

- `scheduled`
- `manual`
- `backfill`

### 3.20 `ingestion_items`

Per-channel/video/link/repo work item status.

Columns:

- `id uuid primary key`
- `ingestion_run_id uuid not null references ingestion_runs(id)`
- `item_type text not null`
- `item_id uuid null`
- `external_key text null`
- `stage text not null`
- `status text not null`
- `retry_count integer not null default 0`
- `is_retryable boolean not null default true`
- `error_summary text null`
- `started_at timestamptz null`
- `completed_at timestamptz null`
- `created_at timestamptz not null`

### 3.21 `domain_events`

Important application/domain events and warning/error summaries.

Columns:

- `id uuid primary key`
- `event_type text not null`
- `severity text not null`
- `entity_type text null`
- `entity_id uuid null`
- `ingestion_run_id uuid null references ingestion_runs(id)`
- `message text not null`
- `details_json jsonb null`
- `created_at timestamptz not null`

Retention default:

- Warning/error/domain summaries: 90 days unless configured.
- Ingestion run summaries: retained longer/indefinitely.

### 3.22 `classification_corrections`

User corrections for link classification learning.

Columns:

- `id uuid primary key`
- `external_link_id uuid not null references external_links(id)`
- `domain text null`
- `previous_classification text not null`
- `corrected_classification text not null`
- `correction_note text null`
- `created_at timestamptz not null`

Used for:

- Deterministic domain allow/block/classification lists.
- Few-shot examples in local LLM prompts.

### 3.23 `notifications`

Notification audit records.

Columns:

- `id uuid primary key`
- `ingestion_run_id uuid null references ingestion_runs(id)`
- `provider text not null default 'matrix'`
- `target text not null`
- `status text not null`
- `message_summary text null`
- `provider_message_id text null`
- `error_summary text null`
- `sent_at timestamptz null`
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
- External link metadata: effective title/description/classification/domain.
- Scraped page text: effective page title/description/visible text.
- Repository README chunks: effective README content.
- Notes: effective markdown.

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

- `combined_score = textWeight * normalizedTextScore + vectorWeight * normalizedVectorScore`
- Return score components for explainability.
- Include matched field/snippet.

## 7. Retention

- Raw transcripts: retain indefinitely unless video/channel deleted.
- Screenshots: retain indefinitely unless purged/deleted.
- Ingestion run summaries: retain indefinitely or configurable long retention.
- Detailed ingestion events: 90 days by default.
- Logs/metrics/traces: retained in observability stack for 90 days.
- Old embeddings: delete when replaced unless later version history is required.

## 8. Deletion behavior

Channel deletion options:

- Stop future ingestion only.
- Delete channel and all related videos, segments, transcripts, links, repositories associations, notes, search documents, embeddings, screenshots.

Repository records may be shared by multiple videos/links. If deleting a channel, remove associations and delete repository only when no remaining associations exist, unless user requests force purge.

Screenshots and raw debug captures must be removed from file volumes when corresponding records are purged.

## 9. Migration notes

Use EF Core migrations for core schema and raw SQL for:

- Extensions.
- pgvector columns/indexes.
- full-text generated columns/indexes if used.
- trigram indexes.
- specialized search functions/views.

All migrations should be idempotent where practical for deployment reliability.
