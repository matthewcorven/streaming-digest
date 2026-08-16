# Backup Manifest Schema

This document defines the backup manifest schema, verification status semantics, and retention policy requirements.

## Manifest Structure

Each backup archive (`.zip` file) must contain a `manifest.json` file at the root. This manifest describes the backup's contents, creation time, schema version, and verification status.

### manifest.json Schema

```json
{
  "createdAtUtc": "2026-08-15T20:30:00Z",
  "backupFileName": "backup-2026-08-15-203000.zip",
  "schemaVersion": "1.0.0",
  "verificationStatus": "verified",
  "restoreTarget": "streaming-digest@db1.example.com",
  "assets": [
    {
      "name": "database",
      "status": "included",
      "path": "db/dump.sql",
      "details": "PostgreSQL full dump with pgvector extensions"
    },
    {
      "name": "media",
      "status": "included",
      "path": "media/assets.tar.gz",
      "details": "Media library archive"
    }
  ]
}
```

### Required Fields

| Field | Type | Description | Example |
|-------|------|-------------|---------|
| `createdAtUtc` | ISO 8601 string | UTC timestamp when the backup was created | `2026-08-15T20:30:00Z` |
| `backupFileName` | string | Original backup archive filename | `backup-2026-08-15-203000.zip` |
| `schemaVersion` | semver | Schema version; must be present and valid | `1.0.0` |
| `verificationStatus` | enum | Status of backup verification (see below) | `verified` |
| `restoreTarget` | string | Target system identifier where backup can be restored | `streaming-digest@db1.example.com` |
| `assets` | array | List of backup assets/components included | See Asset Schema below |

### Asset Schema

Each asset in the `assets` array describes a component included in the backup:

```json
{
  "name": "string",
  "status": "included|excluded|partial",
  "path": "string or null",
  "details": "string or null"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Asset name (e.g., `database`, `media`, `matrix`) |
| `status` | enum | `included`, `excluded`, or `partial` |
| `path` | string or null | Path within backup archive, or null if not applicable |
| `details` | string or null | Optional additional details about the asset |

## Verification Status Enum

The `verificationStatus` field indicates whether the backup has been validated and is ready for restoration.

### Status Values

| Status | Meaning | Health Impact |
|--------|---------|---------------|
| `pending` | Backup created but not yet verified | **Unhealthy** — incomplete backup process |
| `in_progress` | Verification is underway | **Unhealthy** — not yet ready for recovery |
| `completed` | Verification completed but results pending review | **Unhealthy** — awaiting operator sign-off or automated post-verification checks |
| `verified` | Backup fully verified and ready for restoration | **Healthy** — meets all requirements, can be used for recovery |
| `failed` | Verification failed; backup is corrupted or incomplete | **Unhealthy** — must not be used for restoration |

### Semantics

- **Only `verified` indicates a healthy, production-ready backup.** Backups with any other status should be treated as incomplete or at-risk.
- `completed` means the backup process and initial validation completed successfully, but additional checks (retention policy, operator approval, etc.) are still pending before it is considered operationally authoritative.
- `pending` and `in_progress` are transient states that should resolve within minutes.
- `failed` requires operator investigation and archive removal.

## Retention Policy

Backup readiness health is determined by two criteria:

### 1. Verification Status

The latest backup must have `verificationStatus: "verified"` (see above).

### 2. Retention Compliance

Backups must meet both of these requirements:

| Policy | Default | Description |
|--------|---------|-------------|
| **MaxAgeHours** | 72 (3 days) | Latest backup must be no older than this age in hours |
| **MinimumBackupCount** | 2 | At least this many backup archives must exist in the destination directory |

### Retention Logic

```
isHealthy = (latest_backup.verificationStatus == "verified") 
            && (latest_backup.age <= MaxAgeHours) 
            && (backup_archive_count >= MinimumBackupCount)
```

**Example scenarios:**

| Scenario | Latest Status | Latest Age | Archive Count | Compliant? | Reason |
|----------|---------------|------------|---------------|------------|--------|
| ✅ Compliant | `verified` | 24h | 3 archives | **Yes** | Meets all requirements |
| ❌ Expired | `verified` | 96h | 3 archives | **No** | Latest backup exceeds MaxAgeHours |
| ❌ Not Verified | `completed` | 12h | 3 archives | **No** | Verification status is not `verified` |
| ❌ Insufficient | `verified` | 36h | 1 archive | **No** | Below MinimumBackupCount |
| ❌ Failed | `failed` | 12h | 3 archives | **No** | Verification failed |

## Configuration

Retention policy is configured in `appsettings.json` under the `backup` section:

```json
{
  "backup": {
    "destinationPath": "/var/backups/streaming-digest",
    "mediaPath": "/var/lib/streaming-digest/media",
    "matrixPath": "/var/lib/streaming-digest/matrix",
    "includeAppSettings": true,
    "includeSecrets": true,
    "maxAgeHours": 72,
    "minimumBackupCount": 2
  }
}
```

## Live Verification (ADR-0018)

The `/api/admin/health` endpoint's `BackupReadiness` section implements live verification of backup manifests:

- **PreviewMode: `true`** — Backup readiness signal is preview state (under active development per ADR-0018)
- **Source:** `IBackupManifestChecker.GetBackupReadinessAsync()` reads manifest.json from the latest backup archive
- **Schema validation:** Required fields (createdAtUtc, schemaVersion, verificationStatus) are validated; missing or invalid fields trigger an error response
- **Retention check:** Backup age and archive count are validated against policy before determining final health state

### Response Details

The health snapshot includes:

```json
{
  "backupReadiness": {
    "state": "Ready|Degraded|Error",
    "summary": "Backup verified and compliant|...",
    "lastBackupAt": "2026-08-15T12:00:00Z",
    "timeSinceLastBackup": "2h ago",
    "retentionStatus": "Compliant|Attention required",
    "details": [
      "Backup: backup-2026-08-15-120000.zip",
      "Schema version: 1.0.0",
      "Verification status: verified",
      "Backup age: 2.0h (policy: max 72h)",
      "Backup count: 3 (policy: min 2)",
      "Restore target: streaming-digest@db1.example.com",
      "Assets: 2"
    ],
    "previewMode": true
  }
}
```

## See Also

- ADR-0018: Consolidate admin health contract; implement live models endpoint; document live vs preview signals
- `/api/admin/health` in API_SPEC.md
