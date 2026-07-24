### Task 17.4: Document and test restore runbook

Requirements:

- Document restore procedure for PostgreSQL, screenshots/media, Matrix bot session/config, app config, and secrets.
- Restore to a fresh Compose stack during validation rather than only checking that backup files exist.
- Record backup artifact metadata and verification status in `backup_artifacts`/maintenance operations.
- Keep polished automated restore UI, scheduled backups, and CLI backup/restore as MVP+.

Verification:

- Restore dry run validates login, search, screenshots, Matrix test send, and embedding test.
- Restore docs clearly distinguish MVP Matrix bot session/config from MVP+ E2EE crypto-store restore verification.
- Restore dry-run evidence (what was restored, validations run, outcome) committed per the Verification evidence convention.

