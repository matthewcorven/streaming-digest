### Task 0.3: Add schema-validated application config file

Requirements:

- Use a JSON config file validated against a JSON Schema on startup.
- Treat environment variables as deployment/bootstrap inputs and secrets, not the primary mutable settings store.
- UI-editable settings persist to config file or database-backed app settings according to mutability and secret-sensitivity.
- Startup reports clear schema validation errors.

Verification:

- Invalid config fails startup with actionable message.
- UI setting changes survive container restart when persisted to the configured mutable store.

