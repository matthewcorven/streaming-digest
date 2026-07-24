### Task 17.1: Aspire-to-Compose deployment

Requirements:

- Compose project/base name: `streaming-digest`.
- Containers named `streaming-digest-*`.
- One command starts all containers.
- Internal-only ports for internal services.

Verification:

- Fresh host can start stack from documented command.
- Cross-platform dev note (per `docs/architecture/ARCHITECTURE.md` §12): documented manual verification that the Aspire AppHost and key dev flows start on macOS ARM and Windows ARM, with CPU fallback confirmed where GPU is unavailable.
- Cross-platform startup checklist results (macOS ARM, Windows ARM, CPU fallback) committed per the Verification evidence convention.

