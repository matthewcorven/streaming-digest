### Task 15.2: Add production observability Compose services

Services:

- OTel Collector.
- Prometheus.
- Grafana.
- Loki.
- Tempo.

Policy:

- Included in Compose.
- Default-on for localhost development.
- Default-off elsewhere unless enabled during first run or toggled on demand in UI.
- UI links render only when observability is enabled.
- When disabled, the API container/reverse proxy serves placeholder observability pages on the usual routes/ports with instructions to re-enable. When enabled, the same API/reverse-proxy paths route to real observability services.
- Retention selected by first-run free space: 90 days when > 5 GB, 30 days when > 1 GB, disabled with warning otherwise.

Verification:

- `docker compose up` starts stack.
- Grafana dashboards reachable through API/reverse-proxy routes when enabled.
- Disabled mode shows API-served placeholder guidance instead of broken links.

