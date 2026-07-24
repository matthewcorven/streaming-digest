### Task 10.2: Add robots.txt and rate limiting

Source: `docs/architecture/ARCHITECTURE.md` §9

Requirements:

- Per-host rate limit.
- Respect robots.txt by default. If denied, skip scrape but store link.
- Per-domain user override in app configuration.
- First page only.

Verification:

- Local test robots.txt disallow case stores link and skips scrape.
- Per-domain override allows scrape in controlled fixture.

