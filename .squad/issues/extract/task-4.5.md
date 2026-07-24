### Task 4.5: Implement rate-limit deferment service

Source: `docs/architecture/DATA_MODEL.md` §3.30; `docs/architecture/ARCHITECTURE.md` §4.9

Requirements:

- Persist host/dependency deferments in `rate_limit_deferments` for YouTube, repository hosts, DeepWiki, and website hosts.
- Workers check active deferments before starting host-scoped work.
- Resume after `Retry-After` when present, otherwise after the configured default delay.
- Dashboard and ingestion-run details show active deferments prominently.
- Matrix/web daily digest includes active deferments when configured.
- Manual clear endpoint is available for careful operator override.

Verification:

- Repository, DeepWiki, website, and YouTube rate-limit fixtures create deferments and prevent new host-scoped work until expiry/clear.
- Dashboard/API exposes active, expired, and cleared deferments.

