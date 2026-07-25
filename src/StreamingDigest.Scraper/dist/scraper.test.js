import assert from 'node:assert/strict';
import test from 'node:test';
import { normalizeScrapeRequest } from './scraper.js';
test('normalizeScrapeRequest rejects invalid URLs', () => {
    assert.throws(() => normalizeScrapeRequest({ url: 'not-a-url' }), /valid absolute URL/);
});
test('normalizeScrapeRequest applies sane defaults', () => {
    const request = normalizeScrapeRequest({ url: 'https://example.com/path' });
    assert.equal(request.url, 'https://example.com/path');
    assert.equal(request.respectRobotsTxt, true);
    assert.equal(request.timeoutSeconds, 30);
});
