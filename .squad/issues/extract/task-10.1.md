### Task 10.1: Build Crawlee/Playwright scraper service

Inputs:

- URL.
- robots setting and per-domain override setting.
- debug raw HTML flag.
- timeout.

Supported:

- PDFs.
- JavaScript-rendered pages with Playwright JS enabled.
- CDN URLs.
- Displayed/visible text from HTML.
- Non-tracking redirects, preserving original and resulting URL.

Excluded:

- Login pages.
- Tracking redirects.
- Non-PDF file downloads.
- Hidden/invisible element text.
- Raw HTML by default.

Outputs:

- final URL.
- title.
- description.
- OpenGraph/Twitter metadata.
- visible text.
- robots allowed.
- debug raw HTML path optional.
- exclusion reason when skipped.

Verification:

- Scrapes local test page.
- Excluded URLs create partial failure records skipped from retry unless URL changes.

