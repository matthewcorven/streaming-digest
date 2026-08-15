# Configuration Continuity: Dev → Production Deployment Guide

This guide explains how configuration flows across Streaming Digest deployment paths, helping users transition from local development (Aspire) to production (Docker Compose) without losing settings.

## Quick Reference: Three-Layer Configuration Model

| Layer | Location | Scope | Persistence | Used By |
|-------|----------|-------|-------------|---------|
| **Environment** | `.env` file / Docker secrets | Service runtime variables | Per deployment | All services |
| **Application** | `appsettings*.json` | Schema-validated config | JSON file | API, Worker, AppHost |
| **Runtime/User** | PostgreSQL `app_settings` table | User preferences & UI state | Database | API runtime, Hangfire jobs |

### Layer Precedence (Highest → Lowest Priority)

1. **Environment Variables** (`.env`, Docker secrets, Compose `environment` block)
   - Override everything at runtime
   - Never persisted to app config JSON
   - Examples: `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`, `POSTGRES_PASSWORD=...`

2. **Application Config** (`appsettings.json`, `appsettings.{Environment}.json`)
   - Versioned with code, tracked in git
   - Loaded at app startup
   - Environment-specific overrides (Development vs Production)
   - Includes schema validation and migration support
   - Examples: logging levels, API configuration, backup paths

3. **Runtime/Persistent Settings** (PostgreSQL `app_settings`)
   - Set by admin UI or API endpoints
   - Survives app restarts
   - User preferences and operational choices
   - Examples: UI theme, feature flags, notification settings

---

## Configuration Layers in Detail

### Layer 1: Environment Variables

**Where they come from:**

```bash
# Aspire (local development)
dotnet run --project src/StreamingDigest.AppHost
# Reads from: user-secrets (secure storage)

# Docker Compose (production)
docker compose up -d
# Reads from: .env file (or docker-compose.override.yml)
```

**Key environment variables:**

| Variable | Format | Aspire | Compose | Purpose |
|----------|--------|--------|---------|---------|
| `ASPIRE_ALLOW_UNSECURED_TRANSPORT` | bool | `true` | `false` | Allow HTTP (dev only) |
| `POSTGRES_PASSWORD` | string | user-secrets | `.env` | PostgreSQL admin password |
| `POSTGRES_USERNAME` | string | user-secrets | `.env` | PostgreSQL user |
| `GRAFANA_ADMIN_PASSWORD` | string | user-secrets | `.env` | Grafana access |
| `NOTIFICATIONS_MATRIX_*` | string | user-secrets | `.env` | Matrix bot credentials |

**Important: Environment variables are NOT merged into `appsettings.json`**

They override at runtime only. Configuration JSON is read once at startup, then environment variables override specific values.

### Layer 2: Application Configuration (`appsettings.json`)

**Files involved:**

```
src/StreamingDigest.Api/
  ├── appsettings.json              # Base configuration (Aspire + Compose)
  ├── appsettings.Development.json  # Dev-only overrides (local only)
  ├── appsettings.schema.json       # JSON schema for validation

src/StreamingDigest.Worker/
  ├── appsettings.json              # Worker-specific config
  ├── appsettings.Development.json  # Dev overrides
  └── appsettings.schema.json       # Validation schema
```

**Which file applies when?**

| Scenario | Files Loaded | Order |
|----------|--------------|-------|
| **Aspire (dotnet run)** | appsettings.json + appsettings.Development.json | Base first, then Development overrides |
| **Docker Compose (prod)** | appsettings.json only | No Development overrides |
| **Published app** | appsettings.json | Respects ASPNETCORE_ENVIRONMENT env var |

**Example: Connection String**

```json
// appsettings.json (base)
{
  "connectionStrings": {
    "streamingdigest": "Host=postgres;Port=5432;Database=streamingdigest;Username=postgres;Password=postgres"
  }
}

// appsettings.Development.json (local override)
{
  "connectionStrings": {
    "streamingdigest": "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;Password=dev-password"
  }
}

// In Compose (production): appsettings.json is used, env vars can override
// POSTGRES_PASSWORD in .env is passed to container, but appsettings.json doesn't read it automatically
// → Aspire AppHost wires environment → container networking
```

**Configuration Schema Versioning:**

```json
// appsettings.json
{
  "appVersion": "0.8.1",                    // Identifies running software version
  "configSchemaVersion": "1.0.0",           // For JSON config compatibility
  "deploymentSchemaVersion": "1.0.0"       // For Compose/service compatibility
}
```

If you upgrade the app and config schema changes:
- Auto-migrates defaultable keys
- Validates against `appsettings.schema.json`
- On error: shows exact JSON path and expected schema

### Layer 3: Runtime/Persistent Settings (PostgreSQL)

**Stored in:**

```sql
-- Table structure (conceptual)
CREATE TABLE app_settings (
  key TEXT PRIMARY KEY,           -- Unique setting identifier
  value JSONB,                    -- Typed setting value
  created_at TIMESTAMP,
  updated_at TIMESTAMP
);

-- Examples
SELECT * FROM app_settings;
-- key: 'ui.theme'              value: '"dark"'
-- key: 'notifications.enabled' value: 'true'
-- key: 'backup.last_run'       value: '"2026-08-14T12:00:00Z"'
```

**Set via:**

1. Admin UI dashboard (Settings tab)
2. API endpoints (`POST /api/admin/settings`)
3. First-run initialization (wizard)
4. Backup/restore procedures

**Survives:**

- App restarts
- Database restarts
- Configuration migrations
- Does NOT survive full database wipe

**Examples:**

| Key | Value Type | Set By | Persisted? | Example |
|-----|------------|--------|-----------|---------|
| `ui.theme` | string | User preference | ✓ | "dark" |
| `ui.defaultLanguage` | string | Admin | ✓ | "en" |
| `notifications.enabled` | bool | Admin | ✓ | true |
| `backup.lastRun` | ISO8601 | System | ✓ | "2026-08-14T12:00:00Z" |
| `feature.vectorSearch` | bool | Admin | ✓ | true |

---

## Development → Production Transition: Complete Checklist

### Phase 1: Prepare Environment (Aspire Development)

```bash
# 1. Start Aspire AppHost (dev configuration)
dotnet run --project src/StreamingDigest.AppHost

# 2. Configure via Aspire dashboard or user-secrets
dotnet user-secrets set Notifications:Matrix:Enabled "true"
dotnet user-secrets set Notifications:Matrix:AccessToken "syt_..."
dotnet user-secrets set Notifications:Matrix:RoomId "!room:example.com"

# 3. Set user preferences via Admin UI
# Dashboard → Settings → Set theme, language, enable features
# This writes to PostgreSQL app_settings table

# 4. Run workloads
# Ingestion, embedding, search, etc.

# 5. Export configuration (BEFORE moving to production)
docker compose exec streaming-digest-postgres pg_dump -U postgres streaming_digest > dev-config-backup.sql
```

### Phase 2: Generate Production Compose

```bash
# 1. Regenerate compose.yaml from AppHost
./scripts/publish_compose.sh

# 2. Create .env for production
cp .env.example .env
# Edit .env with production values:
# - Strong POSTGRES_PASSWORD
# - Production API secrets
# - Matrix bot token (if enabled)
# - Backup paths
# - Notification settings

# 3. Review compose.yaml changes
git diff compose.yaml
# Should see updated service versions, image tags, etc.
# If custom changes exist, preserve them using docker-compose.override.yml
```

### Phase 3: Start Production Stack

```bash
# 1. Start services with production config
docker compose up -d

# 2. Wait for initialization (PostgreSQL + models)
docker compose logs -f streaming-digest-api | grep -i "ready\|started"

# 3. Check health
curl http://localhost:5000/health
# Should return 200 OK with service status

# 4. Run preflight checks
./scripts/preflight_aspire_parameters.sh
# Verifies all services are responding
```

### Phase 4: Migrate User Configuration

```bash
# OPTION A: Copy app_settings from dev database (recommended)
# 1. Export from dev database
docker exec <dev-container> pg_dump -U postgres -t app_settings streaming_digest > app_settings_export.sql

# 2. Import to production database
docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest < app_settings_export.sql

# 3. Verify import
docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest -c "SELECT key, value FROM app_settings LIMIT 10;"

# OPTION B: Manually reconfigure in production UI
# 1. Open Admin dashboard: http://localhost:5000/admin
# 2. Go to Settings → restore user preferences
# 3. Set theme, language, enable features
# Note: Takes longer, but cleanest for security-sensitive configs
```

### Phase 5: Verify Configuration

```bash
# 1. Check PostgreSQL config is in place
docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest \
  -c "SELECT key, value FROM app_settings WHERE key LIKE 'ui.%' OR key LIKE 'notifications.%';"

# 2. Verify application config loaded
curl http://localhost:5000/api/admin/config-status | jq '.configSchemaVersion'

# 3. Test workloads
# Run manual ingestion:
curl -X POST http://localhost:5000/api/admin/ingest \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  -d '{"sources": ["matrix"], "limit": 10}'

# 4. Check logs for errors
docker compose logs streaming-digest-api | tail -20
docker compose logs streaming-digest-worker | tail -20
```

---

## Configuration Precedence and Override Rules

### When a setting exists in multiple layers:

```
PRECEDENCE (highest to lowest):

1. Environment variables (.env, docker-compose.override.yml)
   Example: POSTGRES_PASSWORD=prod-secret

2. appsettings.json (versioned with app)
   Example: connectionStrings.streamingdigest

3. appsettings.{Environment}.json (Development only, NOT in production)
   Example: appsettings.Development.json logging level

4. PostgreSQL app_settings (user preferences)
   Example: ui.theme = "dark"
```

### Real-world example:

**Dev environment (Aspire):**
```
1. user-secrets set Notifications:Matrix:Enabled true
   ↓
2. appsettings.Development.json: logging = "Debug"
   ↓
3. app_settings table: ui.theme = "dark"
   ↓
RESULT: Notifications enabled, debug logging, dark theme
```

**Production (Compose):**
```
1. .env: NOTIFICATIONS_MATRIX_ENABLED=false
   (⚠️  Environment overrides everything)
   ↓
2. appsettings.json: logging = "Information"
   ↓
3. app_settings table: ui.theme = "light"
   ↓
RESULT: Notifications disabled, info logging, light theme
```

---

## Secret Management Strategy

### Aspire Development (local)

**Where secrets are stored:**

```bash
# User-secrets (encrypted, per-user)
~/.microsoft/usersecrets/<project-id>/secrets.json

# How to set:
dotnet user-secrets set Notifications:Matrix:AccessToken "syt_..."

# Never in:
- appsettings.Development.json (committed to git)
- Environment variables (shell history)
```

### Docker Compose Production

**Where secrets are stored:**

```bash
# Option 1: .env file (simple, less secure)
POSTGRES_PASSWORD=your-strong-password

# Option 2: Docker secrets (secure, requires Docker Swarm/Kubernetes)
docker secret create postgres_password <(echo 'your-strong-password')

# Option 3: External secret manager (vault, AWS Secrets Manager, etc.)
# Configure via Compose labels: com.example.secret_provider=vault
```

**Security checklist:**

```
✓ Never commit .env to git (add to .gitignore)
✓ Rotate secrets quarterly
✓ Use strong random passwords (32+ chars, mixed case/numbers/symbols)
✓ In production: use Docker secrets or external secret manager
✓ Backup encrypted (include --include-secrets in backup path)
✓ Document secret rotation procedure for your team
```

### Rotating Secrets

**PostgreSQL password change:**

```bash
# 1. Set new password in .env
POSTGRES_PASSWORD=new-strong-password

# 2. Update Postgres user
docker compose exec streaming-digest-postgres psql -U postgres -c \
  "ALTER USER postgres WITH PASSWORD 'new-strong-password';"

# 3. Restart services (if password cached in connection strings)
docker compose restart streaming-digest-api streaming-digest-worker

# 4. Verify
docker compose logs streaming-digest-api | grep -i "connection\|connected"
```

**Matrix access token change:**

```bash
# 1. Generate new token in Matrix (Synapse admin panel or client)
# 2. Update .env
NOTIFICATIONS_MATRIX_ACCESS_TOKEN=new_token

# 3. Restart notification service
docker compose restart streaming-digest-api

# 4. Test
curl -X POST http://localhost:5000/api/admin/send-test-notification \
  -H "Authorization: Bearer <admin-token>"
```

---

## Configuration Migration and Compatibility

### Upgrading Configuration Schema

**When AppHost code adds a new config key:**

1. **Auto-migration (defaultable settings):**
   ```
   appsettings.schema.json defines the new key with a default value
   ↓
   On app startup: compares appVersion vs configSchemaVersion
   ↓
   If schema is newer: injects default value into running config
   ↓
   JSON file is NOT modified (defaults only in memory)
   ```

2. **Invalid config (blocking):**
   ```
   appsettings.json has a key with wrong type/format
   ↓
   App startup fails with validation error
   ↓
   Exact JSON path and expected schema type shown to user
   ↓
   User must fix JSON or restore from backup
   ```

3. **Version mismatch:**
   ```
   Application code version: 0.8.1
   Config schema version: 0.7.0 (old)
   ↓
   App shows: "Configuration upgrade required. Review /docs/CONFIG_CONTINUITY.md"
   ↓
   User can proceed (if changes are backward-compatible) or abort
   ```

### Example: Adding a Feature Flag

**Code change (Aspire AppHost):**
```csharp
// Program.cs: add new config key
var config = new
{
    features = new { 
        vectorSearch = true,           // ← NEW KEY
        advancedFilters = false 
    }
};
```

**Schema update (appsettings.schema.json):**
```json
{
  "properties": {
    "features": {
      "properties": {
        "vectorSearch": { "type": "boolean", "default": true },     // ← NEW SCHEMA
        "advancedFilters": { "type": "boolean", "default": false }
      }
    }
  }
}
```

**On deployment:**

```bash
# Dev (Aspire):
# appsettings.Development.json is loaded, feature flag reads default or override

# Prod (Compose):
# appsettings.json is loaded, feature flag reads default
# User can override via Admin UI → Settings → Features

# If app is downgraded:
# Old app doesn't understand vectorSearch key
# → Shows warning but continues (ignores unknown keys)
```

---

## Troubleshooting Configuration Issues

### Symptom: App won't start ("Configuration validation failed")

```bash
# 1. Check startup logs
docker compose logs streaming-digest-api | grep -i "config\|error\|schema"

# Expected output:
# ERROR: Configuration error at 'connectionStrings.streamingdigest': expected string, got null

# 2. Verify .env is loaded
docker compose config | grep -i "POSTGRES_PASSWORD"

# 3. Validate JSON syntax
docker run --rm -v $(pwd):/work alpine:latest jq . /work/src/StreamingDigest.Api/appsettings.json

# 4. Restore from backup
docker compose down
docker volume rm streaming-digest_postgres-data
docker compose up -d
docker compose exec streaming-digest-postgres psql -U postgres streaming_digest < backup.sql
```

### Symptom: Settings lost after restart

```bash
# 1. Check if PostgreSQL is persisting data
docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest \
  -c "SELECT COUNT(*) as setting_count FROM app_settings;"

# 2. Verify volume is mounted correctly
docker inspect streaming-digest_postgres-data
# Check "Mountpoint" exists and is not empty

# 3. Check if volume was accidentally deleted
docker volume ls | grep streaming-digest

# If deleted: restore from backup
docker volume create streaming-digest_postgres-data
docker compose up -d
docker compose exec streaming-digest-postgres psql -U postgres streaming_digest < backup.sql
```

### Symptom: Different settings in dev vs production

```bash
# 1. Compare configuration between environments
echo "=== DEV ===" && dotnet user-secrets list --project src/StreamingDigest.AppHost
echo "=== PROD .env ===" && cat .env | grep -v "^#"
echo "=== PROD app_settings ===" && docker compose exec streaming-digest-postgres \
  psql -U postgres -d streaming_digest -c "SELECT key, value FROM app_settings ORDER BY key;"

# 2. Check which layer is winning (precedence)
# Environment variables override everything
# Edit .env to match dev settings

# 3. Re-export from dev and re-import
# See Phase 4 in checklist above
```

### Symptom: Matrix notifications not working in production

```bash
# 1. Verify .env has correct token
grep NOTIFICATIONS_MATRIX .env

# 2. Check API loaded the setting
curl http://localhost:5000/api/admin/config-status | jq '.notifications'

# 3. Send test notification
curl -X POST http://localhost:5000/api/admin/send-test-notification \
  -H "Authorization: Bearer <admin-token>" | jq '.message'

# 4. Check logs for auth errors
docker compose logs streaming-digest-api | grep -i "matrix\|notification\|401\|403"

# 5. Verify token in Matrix admin panel (still valid, not revoked)
```

---

## Configuration Backup and Restore

### Full Configuration Backup

```bash
# 1. Backup PostgreSQL database (includes app_settings)
docker compose exec streaming-digest-postgres pg_dump \
  -U postgres \
  --include-acls \
  --include-blobs \
  streaming_digest > config-backup-$(date +%Y%m%d-%H%M%S).sql

# 2. Backup .env file
cp .env .env.backup-$(date +%Y%m%d-%H%M%S)

# 3. Backup appsettings.json (if customized)
cp src/StreamingDigest.Api/appsettings.json appsettings-backup-$(date +%Y%m%d-%H%M%S).json

# 4. Create archive
tar czf config-backup-$(date +%Y%m%d).tar.gz \
  config-backup-*.sql \
  .env.backup-* \
  appsettings-backup-* \
  compose.yaml

# 5. Store securely
# - Off-site storage (S3, Google Drive, etc.)
# - Encrypted at rest
# - At least weekly automation
```

### Configuration Restore

```bash
# 1. Restore PostgreSQL database
docker compose down
docker volume rm streaming-digest_postgres-data
docker volume create streaming-digest_postgres-data
docker compose up -d streaming-digest-postgres

# Wait for PostgreSQL to start
sleep 10

# 2. Load backup
docker compose exec streaming-digest-postgres psql \
  -U postgres < config-backup-20260814-120000.sql

# 3. Restart dependent services
docker compose restart streaming-digest-api streaming-digest-worker

# 4. Verify restoration
docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest \
  -c "SELECT COUNT(*) as restored_settings FROM app_settings;"

# 5. Check logs
docker compose logs streaming-digest-api | tail -20
```

### Selective Configuration Restore (Only app_settings)

```bash
# Use when you only want user preferences, not full database

# 1. Extract app_settings from backup
pg_restore config-backup.sql | grep -A999 "app_settings" | grep -B999 "^CREATE TABLE" > app_settings.sql

# 2. Connect to production database and restore
docker compose exec streaming-digest-postgres psql \
  -U postgres -d streaming_digest \
  -c "DELETE FROM app_settings;"   # Clear existing

docker compose exec streaming-digest-postgres psql \
  -U postgres -d streaming_digest < app_settings.sql

# 3. Verify
docker compose exec streaming-digest-postgres psql -U postgres -d streaming_digest \
  -c "SELECT COUNT(*) FROM app_settings;"
```

---

## Cross-Deployment Synchronization

### Scenario: Running parallel dev and prod instances

**Goal:** Keep settings in sync across Aspire and Docker Compose

**Approach:**

```bash
# 1. Periodically export prod settings
(crontab -l 2>/dev/null; echo "0 2 * * * docker compose exec streaming-digest-postgres pg_dump -U postgres -t app_settings streaming_digest > /backups/prod-app_settings-$(date +\\%Y\\%m\\%d).sql") | crontab -

# 2. Import to dev database before starting Aspire
psql -U postgres -d streaming_digest < /backups/prod-app_settings-20260814.sql

# 3. Start Aspire with synced settings
dotnet run --project src/StreamingDigest.AppHost

# 4. Make local changes, test, verify
# Admin UI → Settings → modify configuration

# 5. Export back to shared location (for team sync)
docker compose exec streaming-digest-postgres pg_dump -U postgres -t app_settings streaming_digest > shared-settings.sql
```

### Scenario: Multiple instances (high availability)

**Shared PostgreSQL instance with multiple API/Worker replicas:**

```yaml
# docker-compose.yaml (multi-instance)
services:
  postgres:
    # Single shared database
    image: pgvector/pgvector:...
    volumes:
      - postgres-data:/var/lib/postgresql

  api-1:
    # Replica 1 reads from shared database
    depends_on:
      - postgres
  
  api-2:
    # Replica 2 reads from shared database
    depends_on:
      - postgres

  worker-1:
    # Worker replica 1
    depends_on:
      - postgres

  worker-2:
    # Worker replica 2
    depends_on:
      - postgres
```

**Configuration sync:**
- All replicas read appsettings.json from image
- All replicas write to PostgreSQL app_settings (single source of truth)
- Settings are eventually consistent (within seconds)
- No manual sync needed between instances

---

## Summary: Configuration Decision Tree

```
Do you need to change configuration?
│
├─ Runtime setting (theme, notifications, features)
│  └─ Use Admin UI → Settings tab (writes to PostgreSQL)
│
├─ Application logic config (logging, backup paths)
│  ├─ In Aspire → edit appsettings.Development.json
│  └─ In Compose → edit appsettings.json, restart app
│
├─ Service environment (passwords, ports, secrets)
│  ├─ In Aspire → dotnet user-secrets set ...
│  └─ In Compose → edit .env, restart docker compose
│
└─ AppHost definition (add service, change networking)
   ├─ Edit src/StreamingDigest.AppHost/Program.cs
   ├─ Run ./scripts/publish_compose.sh
   └─ Commit updated compose.yaml
```

---

## See Also

- **DEPLOYMENT_MODELS.md** — Which deployment path is right for you?
- **ASPIRE_COMPOSE_WORKFLOW.md** — How Aspire AppHost generates Compose
- **UPGRADE_PATHS.md** — Configuration migration and upgrade strategies
- **ARCHITECTURE.md § Config Model** — Design decisions behind this structure
