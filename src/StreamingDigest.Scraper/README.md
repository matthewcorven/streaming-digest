# Streaming Digest scraper service

## Local development

- `npm ci`
- `npm run build`
- `npm test`
- `npm run install:playwright` to install the Chromium browser used by Playwright.

## Docker

```bash
docker build -t streaming-digest-scraper ./src/StreamingDigest.Scraper
docker run --rm -p 3000:3000 streaming-digest-scraper
```

The Docker build installs the Playwright Chromium browser as part of the image build so the scraper starts with the same browser runtime as local development.

The service exposes:

- `GET /health`
- `POST /internal/scrape/first-page`
