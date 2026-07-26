# Streaming Digest Upgrade Paths

Status: MVP upgrade policy draft

This document defines the accepted upgrade paths and edge cases for Streaming Digest. It assumes the MVP configuration split agreed elsewhere:

- Docker environment variables and Docker secrets are for bootstrap, secrets, service wiring, runtime environment, and mounted volume paths.
- Schema-validated JSON config is for durable runtime/deployment configuration and first-run outputs that must survive restarts.
- PostgreSQL app settings are for user-facing product behavior, onboarding/readiness state, operational state, and domain data.

The product rule is: users should usually be able to click **Backup**, update, and restart. The app must still distinguish safe app-only upgrades from deployment migrations that require Compose, container, port, or volume changes.

## 1. Upgrade categories

| Category | What changes | Compose/deployment change required | DB/config migration likely | User-facing risk level | Required user action |
|---|---|---|---|---|---|
| Safe app-only upgrade | API, worker, Matrix notifier, scraper, or UI code changes with the same container topology | No | No or additive/defaultable | Low | Pull new images and restart |
| App upgrade with data migration | App code plus EF migration, config schema migration, or DB app-setting seed | No | Yes | Medium | Backup recommended, then restart/apply migration |
| App upgrade with derived-data regeneration | Ranking/search/embedding behavior changes derived data but not deployment topology | No | Maybe | Medium | Confirm any destructive invalidation; let background jobs regenerate |
| Deployment/Compose migration | Service list, service names, ports, profiles, networks, env/secret sources, or volume paths change | Yes | Maybe | High | Backup strongly recommended or required; apply Compose instructions |
| High-risk infrastructure migration | PostgreSQL major version, pgvector/index rebuild, Matrix crypto store change, large volume migration | Yes | Yes | Critical | Backup required; follow manual or guided migration runbook |

## 2. Version model

Streaming Digest should track four compatibility versions so the UI and startup checks can explain exactly what is wrong.

| Version | Stored where | Updated by | Purpose | Compatibility failure behavior |
|---|---|---|---|---|
| `appVersion` | Image/build metadata and diagnostics endpoint | Build pipeline | Identifies running software version | If older than DB/config/deployment versions, startup warns or blocks downgrade |
| `dbSchemaVersion` | EF migrations table in PostgreSQL | EF migrations | Identifies database schema compatibility | If older than app requires, run migration; if newer than app supports, block startup |
| `configSchemaVersion` | JSON config file | Config migrator | Identifies config-file compatibility | If older, auto-migrate defaultable changes; if invalid/newer, enter maintenance/setup error state |
| `deploymentSchemaVersion` | JSON config or generated deployment metadata | Compose/deployment generation | Identifies service/volume/profile compatibility | If older than app requires, show deployment migration required and block dependent services |

For image rollouts, keep Compose tags versioned with both software and deployment schema identity (for example, `ghcr.io/<org>/streaming-digest-api:v{appVersion}-deploy.{deploymentSchemaVersion}` and the matching worker tag). This prevents app-only tag bumps from skipping required deployment migrations.

## 3. A: App/service changes only

App/service-only upgrades keep the same container topology, mounted volumes, public/internal ports, Compose project name, service names, and external dependency set.

### 3.1 Typical app/service-only changes

| Change type | Example | Deployment shape changes | Expected upgrade path | Edge-case handling |
|---|---|---|---|---|
| API/backend code change | New endpoint or retry behavior | No | Pull image, restart API/worker, run startup checks | Worker waits for DB/config compatibility before processing jobs |
| Blazor UI change | New result-card layout | No | Pull API/Web image and restart | Hosted WASM assets should be version-aligned with API; PWA service worker update flow applies (see §3.3) |
| Worker logic change | Better yt-dlp parsing | No | Pull worker image and restart | Already processed videos are not reprocessed unless explicitly retried |
| Matrix notifier code change | Improved message formatting | No | Pull notifier image and restart | Existing Matrix crypto store is relevant only when E2EE is enabled after MVP |
| Scraper code change | Improved visible-text extraction | No | Pull scraper image and restart | Existing scraped pages remain as-is unless explicit retry/reprocess occurs |
| Database schema change | Add `user_interaction_events` table | No | Backup recommended, run EF migration on startup | If migration fails, app enters maintenance mode and worker stays paused |
| Config schema addition | Add optional config key with default | No | Auto-migrate JSON config on startup | If user config is invalid, show exact JSON path and expected schema |
| DB app setting addition | Add `search.highSignalThresholdPercent` | No | Seed default setting if missing | Preserve user value if already present |
| Ranking formula change | New cluster score formula version | No | New searches use new formula; no data migration unless specified | Store formula version in diagnostics for explainability |
| LLM prompt/schema change | New segmentation JSON schema | No | Future processing uses new schema | Old segments remain unchanged unless user explicitly regenerates |
| Embedding recommendation change | New recommended Ollama model | No | Show updated recommendation only | Do not silently switch active model |

### 3.2 App/service-only upgrade flow

| Step | Actor | Action | Success state | Failure state |
|---|---|---|---|---|
| 1 | App startup | Compare app, DB, config, and deployment versions | Versions compatible or migration required | Newer DB/config/deployment than app blocks startup |
| 2 | Admin UI/startup log | Recommend backup when DB/config migration is pending | User can run backup or continue when backup is only recommended | Backup failure blocks only if migration is marked backup-required |
| 3 | API/migration runner | Pause workers or enter migration maintenance mode | No ingestion jobs mutate data during migration | Worker refuses to process until migration completes |
| 4 | API/migration runner | Apply EF migrations transactionally where possible | `dbSchemaVersion` reaches required version | Maintenance mode with actionable migration error |
| 5 | Config migrator | Apply JSON config migration and validate schema | Config file has current schema and valid values | Startup shows exact invalid key/path and expected value type |
| 6 | App setting seeder | Seed missing default DB app settings | Missing settings created without overwriting user values | Setting seed failure is logged and blocks only if required for startup |
| 7 | Derived-data scheduler | Mark affected search docs, embeddings, or cluster aggregates stale when required | Regeneration jobs queued or user approval requested | Dashboard shows stale/incomplete status and retry action |
| 8 | Worker | Resume queues after compatibility checks pass | Jobs run under new app version | Worker remains paused/degraded with health reason |
| 9 | Admin UI | Show upgrade result and outstanding actions | User sees success and any stale-data jobs | User sees failed migration/config/dependency action list |

### 3.3 App/service-only edge cases

| Edge case | Risk | Detection | Recommended behavior | User message |
|---|---|---|---|---|
| Migration succeeds but worker config is invalid | Worker corrupts or fails jobs | Worker startup config validation | Keep worker paused until config is valid | “Worker paused: invalid config at `<path>`.” |
| Config key moves from env to JSON/DB | User loses existing setting | Config migrator sees legacy env/config key | Support both for one release, migrate value, warn | “Setting migrated; old env var is deprecated.” |
| User-edited unknown JSON config fields exist | User custom data may be discarded | Config parser sees unknown keys | Preserve unknown keys when safe and warn | “Unknown config keys preserved but ignored.” |
| Migration partially applies | DB schema inconsistent | EF migration failure or migration lock state | Prefer transactions; otherwise mark failed step and block worker | “Migration failed at step X; restore backup or retry after fix.” |
| User downgrades image after migration | Older app cannot read newer DB | App version check sees DB too new | Block startup except explicit documented rollback | “Database was upgraded by newer app; downgrade unsupported.” |
| pgvector extension too old | Vector search/index creation fails | Startup extension/version check | Block vector features or maintenance mode depending severity | “pgvector version unsupported; upgrade database extension.” |
| Search formula changes results | User trust confusion | Formula version differs from previous searches | Show formula version in diagnostics; no migration needed | “Ranking formula updated; old searches are not re-ranked retroactively.” |
| Recent searches use old embedding model | Digest compares incompatible vectors | Embedding provider/model/dimensions mismatch | Ignore or regenerate recent-search embeddings for active model | “Recent-search embeddings are stale and regenerating.” |
| User cancels embedding regeneration | Search/digest incomplete | Pending user approval or cancelled job state | Keep old embeddings ignored for active model and show warning | “Embeddings incomplete for active model.” |
| Queued Hangfire job references old stage name | Retry fails or wrong stage runs | Stable stage-name mapping missing | Map old stage names or cancel and recreate retryable item | “Old queued job converted or cancelled; retry available.” |
| Hangfire serialized type changed | Job deserialization fails | Hangfire exception on job load | Use stable DTOs; mark old jobs cancelled/retryable | “Old background job incompatible; retry from UI.” |
| New validation rejects old accepted config | Upgrade blocks unexpectedly | Config validation failure after migration | Normalize legacy values before final validation | “Config normalized from old format; review settings.” |
| PWA service worker serves stale WASM assets after upgrade | Users run an old UI against a new API (version mismatch) | App version check in UI, or service worker update found event | On upgrade, activate the new service worker promptly and surface an in-app "update available, reload" prompt; WASM/API version handshake warns on mismatch | “A new version of Streaming Digest is ready — reload to update.” |

## 4. B: Docker/container changes

Docker/container upgrades change deployment shape. These changes include new/removed services, service renames, volume moves, port changes, Compose profile changes, network changes, secret-source changes, GPU runtime changes, or observability topology changes.

### 4.1 Typical Docker/container changes

| Change type | Example | App-only upgrade possible | Expected upgrade path | Edge-case handling |
|---|---|---|---|---|
| New service container | Add `streaming-digest-model-downloader` | No | Update Compose file, start service, update deployment schema | App detects missing service and shows Compose update required |
| Removed service container | Merge scraper into worker | No | Update Compose file, migrate config, remove obsolete service | Old service URL ignored with warning for one release |
| Service rename | Rename `streaming-digest-scraper` | No | Add stable alias or migrate service URL | Support old and new names for one release when practical |
| Port change | Grafana or placeholder port changes | No | Update Compose and UI link config | Old bookmarked port should show placeholder or redirect guidance |
| Volume path change | Move screenshots or Matrix crypto path | No | Backup, copy/verify volume data, update config | Never silently create empty replacement volume when old data exists |
| Volume split | Separate media into screenshots/raw-html/temp | No | Copy old data into new volumes and verify counts/checksums | Keep old volume until user confirms cleanup |
| Volume merge | Combine related volumes | No | Copy into new layout and preserve old volume | Cleanup is explicit, not automatic |
| Secret source change | Env var to Docker secret file | No | Support both temporarily; secret file wins | Warn on conflicting env var and secret file values |
| Network change | Internal service hostname changes | No | Update Compose aliases and config schema | Health check verifies API reaches every required service |
| GPU/runtime profile | Optional GPU for Ollama/Whisper | No | Add optional Compose profile; CPU remains default | If GPU unavailable, fall back to CPU and warn |
| Observability topology change | Default-off outside localhost plus API/reverse-proxy placeholders | No | Update Compose profiles and API/reverse-proxy routes | Preserve existing enabled/disabled user choice |
| PostgreSQL major version | PostgreSQL 16 to 17 | No | Special DB upgrade runbook using backup and pg_upgrade or dump/restore | Refuse casual image bump without explicit migration path |
| pgvector/index change | New index strategy or extension version | Maybe | Extension migration plus reindex in maintenance/background mode | Search degraded while indexes rebuild |
| Matrix crypto store change | SDK storage format changes after E2EE is enabled | No | Backup crypto store, migrate, verify encrypted send | Block E2EE readiness until verification succeeds |
| Model volume path change | Ollama/Whisper models move | No | Detect existing model, migrate path, or offer redownload | Missing models degrade setup/readiness but not existing DB data |

### 4.2 Docker/container upgrade flow

| Step | Actor | Action | Success state | Failure state |
|---|---|---|---|---|
| 1 | Current app/admin UI | Run preflight: DB version, config schema, deployment schema, volumes, free space, service health | Upgrade classified accurately | Unknown deployment state requires manual instructions |
| 2 | Admin UI | Classify upgrade as app-only, Compose-compatible, deployment migration, or high-risk infrastructure | User sees risk level and required actions | User cannot proceed without resolving blocking preflight errors |
| 3 | Admin UI | Require or strongly recommend backup based on risk | Backup artifact created or user consciously skips when allowed | Required backup failure blocks migration |
| 4 | Docs/UI | Show exact Compose/profile/env/secret/config changes | User has copy-pasteable instructions | Instructions cannot be generated; show manual runbook link |
| 5 | Operator | Stop worker first, then API if needed; keep DB safe | No jobs mutate data during deployment change | In-flight jobs are cancelled/retryable or allowed to drain |
| 6 | Operator | Pull images and apply Compose update | New service topology starts | Compose failure leaves old backup/restoration path documented |
| 7 | Migration task | Move/copy volumes or rewrite config paths if required | Data present at new paths and verified | Migration aborts and old volume remains untouched |
| 8 | Infrastructure | Start Postgres, Ollama, Whisper, scraper, Matrix, observability services or API/reverse-proxy placeholders | Required services pass health checks | Missing optional services produce warnings; missing required services block |
| 9 | App startup | Apply DB/config migrations after deployment compatibility passes | App reaches normal or degraded-ready state | Maintenance mode shows failing dependency/migration |
| 10 | Worker | Resume queues after dependencies are compatible | Jobs resume | Worker remains paused with reason and retry action |
| 11 | Admin UI | Show post-upgrade checklist | User verifies search, screenshots, Matrix, embeddings, backup path | Failed checks show targeted remediation |

### 4.3 Docker/container edge cases

| Edge case | Risk | Detection | Recommended behavior | User message |
|---|---|---|---|---|
| User updates images but not Compose file | App expects missing service or volume | Deployment schema/version check | Block dependent feature and show Compose update required | “Images updated but Compose file is too old.” |
| User updates Compose file but not images | Old app sees unsupported services/config | App version lower than deployment schema | Warn or block if incompatible | “Compose file is newer than app image.” |
| Worker starts before DB migration | Jobs write incompatible data | Worker startup DB version check | Worker waits/refuses to process | “Worker waiting for database migration.” |
| API starts but model service is missing | Onboarding/search features fail | Model service health check | Enter degraded setup state; do not crash basic UI | “Model service unavailable; ingestion/search setup incomplete.” |
| Observability ports bookmarked but disabled | User sees connection errors | Observability disabled setting | API/reverse-proxy placeholder route returns friendly disabled page | “Observability is disabled; enable it in Admin > Operations.” |
| User had observability enabled before upgrade | New default-off disables expected dashboards | Existing app setting says enabled | Preserve user choice over new defaults | “Observability remains enabled from previous configuration.” |
| Matrix crypto path wrong | Bot loses session or cannot decrypt/send | Matrix verification health check | Block Matrix readiness until fixed and test send succeeds | “Matrix crypto store not verified.” |
| Mounted volume permissions changed | Writes fail at runtime | Startup read/write/delete preflight | Block affected service/jobs until fixed | “Volume permission check failed for `<path>`.” |
| Temp folder moved with jobs in flight | Temp media jobs fail | Missing temp files/job state | Cancel/retry affected temp-media stages | “Temp media lost; affected stages will repeat.” |
| Model path changed and model missing | Embeddings/Whisper unavailable | Model discovery health check | Offer redownload, file path, or CLI command | “Model not found at configured path.” |
| Embedding service dimensions changed | Vector comparisons invalid | Provider/model/dimensions mismatch | Block use of mismatched embeddings; require regeneration | “Embedding dimensions changed; regenerate embeddings.” |
| Container timezone changed | 6 AM schedule shifts unexpectedly | Stored user timezone differs from container timezone | Use explicit user timezone and warn if host changed | “Host timezone changed; schedule still uses `<timezone>`.” |
| Postgres data volume missing | App initializes empty DB over existing install | Config indicates prior install but DB empty | Refuse silent fresh initialization | “Existing install expected but database volume appears empty.” |
| Compose project name changed | Docker creates new volumes; data appears gone | Project name differs from stored deployment metadata | Warn strongly and require confirmation/instructions | “Compose project name changed; existing volumes may be detached.” |
| Backup path not mounted | Backup button fails | Startup/backup path preflight | Disable backup button and show config error | “Backup path is not writable or not mounted.” |
| External ports conflict | Compose fails or service unavailable | Compose/startup health check | Allow profile/port override and show conflict | “Port conflict detected; change mapped port or disable service.” |
| Disk retention invalid after volume change | Telemetry fills disk | Free-space recalculation | Recompute retention and warn before enabling telemetry | “Telemetry retention reduced due available disk.” |
| User downgrades deployment topology | Services/data unsupported | Deployment schema newer than app or downgrade marker | Unsupported unless explicit rollback runbook exists | “Deployment downgrade unsupported.” |
| Env var and secret file conflict | Wrong secret used | Secret loader detects both values differ | Secret file wins and warning is logged/displayed | “Docker secret overrides conflicting env var.” |

## 5. Upgrade UX requirements

The Admin UI should include an **Upgrade & Maintenance** panel.

| UI section | Required MVP content | Primary action | Failure behavior |
|---|---|---|---|
| Current versions | App, DB schema, config schema, deployment schema | Copy diagnostics | Show incompatible version in red |
| Upgrade status | Up-to-date, migration available, deployment update required, or high-risk migration required | View migration plan | Disable unsafe actions until blockers resolved |
| Backup status | Last backup time, backup location, backup health | Run backup | Show backup error and block backup-required migrations |
| Migration preview | Pending DB migrations, config migrations, deployment changes, derived-data invalidations | Apply allowed migration | Show exact failing migration/config step |
| Service compatibility | API, worker, scraper, Matrix, Ollama, Whisper, Postgres, observability health | Retry health checks | Mark optional services degraded and required services blocking |
| Derived data status | Stale embeddings, stale search docs, pending segment approvals, index rebuilds | Regenerate or approve | Keep warnings visible until resolved |
| Risk level | Safe, backup recommended, backup required, manual migration required | Open runbook | Require confirmation for high-risk steps |
| Post-upgrade checklist | Login, search, screenshots, Matrix send, embedding test, backup path | Run checks | Show targeted remediation per failed check |

## 6. Restore runbook (MVP / MVP+)

Restore operations should be treated as a recovery path for a fresh Compose stack, not as a backup-file existence check. The archive is the transport; the operator must prove that PostgreSQL, mounted media, Matrix data/config, app config, and secrets can be recovered into the expected Compose-mounted paths.

### 6.1 Restore scope

- PostgreSQL: restore the `postgresql/postgres.sql` asset into the target database with `psql --single-transaction --set ON_ERROR_STOP=1`.
- Screenshots/media: restore `media/` into the configured screenshot/media volume and verify that the restored files are readable from the container or host path.
- Matrix bot session/config: restore `matrix/` into the Matrix configuration/session volume. MVP validates that the session/config files are present and the bot can start; Matrix E2EE crypto/session migration and encrypted-send verification remain MVP+.
- App config and secrets: restore `config/appsettings.json`, `config/appsettings.schema.json`, and `.env` into the fresh Compose stack's config root. Secrets must be re-applied through the same secret/env source used by the deployment.
- Backup artifact metadata: each archive should include manifest metadata with `createdAtUtc`, `backupFileName`, `schemaVersion`, `verificationStatus`, `restoreTarget`, and the asset list. Maintenance operations should record whether restore validation completed and what restore target was validated.

### 6.2 Operator procedure

1. Create or start a fresh Compose stack with the target volume mounts and config paths.
2. Restore the latest backup archive into the fresh stack's media, Matrix, and config locations.
3. Re-apply the app config and secrets expected by the deployment, then restart the affected services.
4. Validate the restored stack by running a restore dry run, checking service health, and confirming the expected files are present at the mounted paths.
5. Record the evidence in the maintenance operation record and the backup artifact manifest so the next operator can see that restore validation completed.

### 6.3 Validation evidence

- Restore dry-run evidence should include the backup filename, manifest metadata, the restore target path, and the validation output from the fresh stack.
- For evidence, save the restore command output, the service health summary, and a short note confirming that the expected PostgreSQL dump, media files, Matrix config/session files, and config/secrets were restored successfully.
- The restore validation is considered successful only when the fresh stack can reach the expected readiness state, not simply when the backup archive exists.

### 6.4 MVP vs MVP+ expectations

| Capability | MVP | MVP+ |
|---|---|---|
| Manual restore from backup archive into a fresh Compose stack | Yes | Yes |
| Restore dry-run validation and evidence capture | Yes | Yes |
| Polished automated restore UI in Admin | No | Yes |
| Scheduled backup jobs | No | Yes |
| CLI backup/restore workflow | No | Yes |
| Matrix E2EE session-store migration and encrypted-send verification | Partial/manual | Yes |

## 7. Non-negotiable upgrade invariants

- Worker must not process jobs against an incompatible DB schema.
- Already processed videos must not be reprocessed during normal daily ingestion because of an app upgrade.
- Embedding model changes must not happen silently because a new app default or recommendation changed.
- Segment regeneration must be explicit user action only.
- Volume migrations must preserve old data until verification succeeds and cleanup is explicitly confirmed.
- Matrix crypto/session store migrations require backup and encrypted-send verification when E2EE is enabled after MVP.
- Compose project/base name `streaming-digest` should be treated as stable deployment identity.
- User choices must beat new defaults during upgrades, especially observability enabled/disabled state and selected models.
