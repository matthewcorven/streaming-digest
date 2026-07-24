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

