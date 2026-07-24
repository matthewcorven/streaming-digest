### Task 1.4: Implement app-settings default seeding

Requirements:

- Seed missing `app_settings` defaults on startup without overwriting existing user values, per `docs/operations/UPGRADE_PATHS.md` app-setting seed rules.
- Seed all settings listed in `docs/architecture/DATA_MODEL.md` §3.2, including `search.highSignalThresholdPercent` (default `80`), `search.interactionBoostWindowDays` (default `90`), text/vector weights, ingestion defaults, `ingestion.scheduleLocalTime` (`06:00`) and `ingestion.scheduleTimeZone` (IANA zone captured from the browser during onboarding), `ingestion.minDurationSeconds` (default `61`), `ingestion.tempMedia.maxBytes` (50% of first-run free disk bytes), screenshot offset, Matrix notification toggles including `notifications.matrix.onBackfillRuns` (default `false`), observability defaults, and debug raw-HTML default.
- Seed failures are logged and block startup only for settings required to boot.

Verification:

- First startup seeds all defaults; second startup preserves user-modified values.
- Upgrade-style startup with a new setting key adds only the missing key.

