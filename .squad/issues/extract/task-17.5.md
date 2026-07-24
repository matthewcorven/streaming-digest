### Task 17.5: Implement upgrade and migration policy

Requirements:

- Follow `docs/operations/UPGRADE_PATHS.md`.
- Track `appVersion`, `dbSchemaVersion`, `configSchemaVersion`, and `deploymentSchemaVersion`.
- Distinguish safe app-only upgrades, app upgrades with data migration, derived-data regeneration upgrades, deployment/Compose migrations, and high-risk infrastructure migrations.
- Versioned Compose tags.
- EF migrations run on startup, with workers blocked until schema compatibility is confirmed.
- UI/docs recommend backup before migration and require backup for high-risk infrastructure migrations.
- Migration failure is surfaced clearly and does not silently corrupt state.

Verification:

- Startup applies migration in integration test.
- Pre-migration backup recommendation appears in upgrade docs/UI.
- Worker refuses to process jobs when DB/config/deployment versions are incompatible.

