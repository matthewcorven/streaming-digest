import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import test from 'node:test';
import { normalizeScrapeRequest, scrapeFirstPage } from './scraper.js';
function createTestServer(handler) {
    const server = createServer(handler);
    return new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            resolve({
                baseUrl: `http://127.0.0.1:${address.port}`,
                close: () => new Promise((closeResolve, closeReject) => {
                    server.close((error) => (error ? closeReject(error) : closeResolve()));
                })
            });
        });
    });
}
test('normalizeScrapeRequest rejects invalid URLs', () => {
    assert.throws(() => normalizeScrapeRequest({ url: 'not-a-url' }), /valid absolute URL/);
});
test('normalizeScrapeRequest applies sane defaults', () => {
    const request = normalizeScrapeRequest({ url: 'https://example.com/path' });
    assert.equal(request.url, 'https://example.com/path');
    assert.equal(request.respectRobotsTxt, true);
    assert.equal(request.timeoutSeconds, 30);
});
test('scrapeFirstPage extracts visible text and metadata from a rendered page', async () => {
    const server = await createTestServer((request, response) => {
        if (request.url === '/robots.txt') {
            response.writeHead(200, { 'content-type': 'text/plain' });
            response.end('User-agent: *\nDisallow:\n');
            return;
        }
        response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        response.end(`<!doctype html>
      <html>
        <head>
          <title>Example title</title>
          <meta name="description" content="Example description" />
          <meta property="og:image" content="https://example.com/cover.png" />
        </head>
        <body>
          <div style="display:none">Hidden content</div>
          <main>
            <h1>Visible heading</h1>
            <p>Visible content</p>
          </main>
        </body>
      </html>`);
    });
    try {
        const response = await scrapeFirstPage({
            url: `${server.baseUrl}/page`,
            debugCaptureRawHtml: true,
            timeoutSeconds: 20
        });
        assert.equal(response.title, 'Example title');
        assert.equal(response.description, 'Example description');
        assert.equal(response.openGraph['og:image'], 'https://example.com/cover.png');
        assert.match(response.visibleText, /Visible content/);
        assert.doesNotMatch(response.visibleText, /Hidden content/);
        assert.equal(response.exclusionReason, null);
        assert.equal(response.rawHtmlDebugPath !== null, true);
    }
    finally {
        await server.close();
    }
});
test('scrapeFirstPage respects robots.txt exclusions', async () => {
    const server = await createTestServer((request, response) => {
        if (request.url === '/robots.txt') {
            response.writeHead(200, { 'content-type': 'text/plain' });
            response.end('User-agent: *\nDisallow: /blocked\n');
            return;
        }
        response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        response.end('<html><body>Blocked content</body></html>');
    });
    try {
        const response = await scrapeFirstPage({ url: `${server.baseUrl}/blocked` });
        assert.equal(response.exclusionReason, 'robots-txt');
        assert.equal(response.robotsAllowed, false);
        assert.equal(response.visibleText, '');
    }
    finally {
        await server.close();
    }
});
test('scrapeFirstPage blocks all paths for robots disallow root', async () => {
    const server = await createTestServer((request, response) => {
        if (request.url === '/robots.txt') {
            response.writeHead(200, { 'content-type': 'text/plain' });
            response.end('User-agent: *\nDisallow: /\n');
            return;
        }
        response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        response.end('<html><body>Should not be scraped</body></html>');
    });
    try {
        const response = await scrapeFirstPage({ url: `${server.baseUrl}/anything` });
        assert.equal(response.exclusionReason, 'robots-txt');
        assert.equal(response.robotsAllowed, false);
    }
    finally {
        await server.close();
    }
});
test('scrapeFirstPage excludes tracking redirects', async () => {
    const server = await createTestServer((request, response) => {
        if (request.url === '/redirect') {
            response.writeHead(302, { location: '/target?utm_source=test' });
            response.end();
            return;
        }
        response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        response.end('<html><body>Redirect target</body></html>');
    });
    try {
        const response = await scrapeFirstPage({ url: `${server.baseUrl}/redirect` });
        assert.equal(response.exclusionReason, 'tracking-redirect');
        assert.match(response.finalUrl, /utm_source=test/);
        assert.equal(response.visibleText, '');
    }
    finally {
        await server.close();
    }
});
test('scrapeFirstPage allows non-tracking redirects and keeps final URL', async () => {
    const server = await createTestServer((request, response) => {
        if (request.url === '/start') {
            response.writeHead(302, { location: '/target' });
            response.end();
            return;
        }
        if (request.url === '/robots.txt') {
            response.writeHead(200, { 'content-type': 'text/plain' });
            response.end('User-agent: *\nDisallow:\n');
            return;
        }
        response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        response.end('<html><head><title>Redirected</title></head><body>Landing page</body></html>');
    });
    try {
        const response = await scrapeFirstPage({ url: `${server.baseUrl}/start` });
        assert.equal(response.exclusionReason, null);
        assert.equal(response.finalUrl, `${server.baseUrl}/target`);
        assert.match(response.visibleText, /Landing page/);
    }
    finally {
        await server.close();
    }
});
test('scrapeFirstPage excludes non-pdf file downloads in query parameters', async () => {
    const server = await createTestServer((request, response) => {
        response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        response.end('<html><body>Should not be scraped</body></html>');
    });
    try {
        const response = await scrapeFirstPage({ url: `${server.baseUrl}/download?file=archive.zip` });
        assert.equal(response.exclusionReason, 'non-pdf-file-download');
        assert.equal(response.visibleText, '');
    }
    finally {
        await server.close();
    }
});
