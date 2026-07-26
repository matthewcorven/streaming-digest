import { createHash } from 'node:crypto';
import { promises as fs } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { chromium } from 'playwright';
const SCRAPER_USER_AGENT = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36 Edg/150.0.0.0';
const MAX_VISIBLE_TEXT_LENGTH = 200_000;
const HOST_RATE_LIMITS = new Map();
export function normalizeScrapeRequest(input) {
    if (!input.url || typeof input.url !== 'string') {
        throw new Error('A non-empty url is required.');
    }
    const trimmedUrl = input.url.trim();
    let parsedUrl;
    try {
        parsedUrl = new URL(trimmedUrl);
    }
    catch {
        throw new Error('The provided url is not a valid absolute URL.');
    }
    if (!['http:', 'https:'].includes(parsedUrl.protocol)) {
        throw new Error('Only http and https URLs are supported.');
    }
    const timeoutSeconds = input.timeoutSeconds ?? 30;
    if (!Number.isInteger(timeoutSeconds) || timeoutSeconds < 5 || timeoutSeconds > 120) {
        throw new Error('timeoutSeconds must be an integer between 5 and 120.');
    }
    return {
        url: parsedUrl.toString(),
        respectRobotsTxt: input.respectRobotsTxt ?? true,
        debugCaptureRawHtml: input.debugCaptureRawHtml ?? false,
        timeoutSeconds,
        rateLimitDelayMs: input.rateLimitDelayMs ?? 1000
    };
}
export async function scrapeFirstPage(request) {
    const normalized = normalizeScrapeRequest(request);
    const parsedUrl = new URL(normalized.url);
    await enforcePerHostRateLimit(parsedUrl.hostname, normalized.rateLimitDelayMs);
    const exclusionReason = detectExcludedUrl(parsedUrl);
    if (exclusionReason) {
        return createExclusionResponse(normalized.url, normalized.url, exclusionReason, 0, null);
    }
    let robotsAllowed = true;
    let robotsReason = null;
    if (normalized.respectRobotsTxt) {
        const robotsRules = await loadRobotsRules(parsedUrl);
        robotsAllowed = isRobotsAllowed(parsedUrl.pathname, robotsRules);
        if (!robotsAllowed) {
            robotsReason = 'robots-txt';
        }
    }
    if (!robotsAllowed) {
        return createExclusionResponse(normalized.url, normalized.url, robotsReason ?? 'robots-txt', 0, null);
    }
    let browser;
    try {
        browser = await chromium.launch({ headless: true });
        const context = await browser.newContext({ userAgent: SCRAPER_USER_AGENT });
        const page = await context.newPage();
        const timeoutMs = normalized.timeoutSeconds * 1000;
        page.setDefaultTimeout(timeoutMs);
        const response = await page.goto(normalized.url, { waitUntil: 'domcontentloaded' });
        const finalUrl = response?.url() ?? normalized.url;
        const exclusionReason = detectRedirectExclusion(normalized.url, finalUrl);
        if (exclusionReason) {
            return createExclusionResponse(normalized.url, finalUrl, exclusionReason, response?.status() ?? 0, null);
        }
        const loginReason = await detectLoginPage(page, finalUrl);
        if (loginReason) {
            return createExclusionResponse(normalized.url, finalUrl, loginReason, response?.status() ?? 0, null);
        }
        const title = await page.title().catch(() => null);
        const description = await page.evaluate(() => document.querySelector('meta[name="description"]')?.getAttribute('content') ?? null).catch(() => null);
        const visibleText = await extractVisibleText(page);
        const openGraph = await page.evaluate(() => {
            const values = {};
            for (const element of Array.from(document.querySelectorAll('meta'))) {
                const property = element.getAttribute('property') ?? element.getAttribute('name');
                const content = element.getAttribute('content');
                if (property && content && (property.startsWith('og:') || property.startsWith('twitter:'))) {
                    values[property] = content;
                }
            }
            return values;
        });
        const contentHash = createHash('sha256').update(`${title ?? ''}\n${visibleText}`).digest('hex');
        const status = response?.status() ?? 0;
        const contentType = response?.headers()['content-type'] ?? null;
        let rawHtmlDebugPath = null;
        if (normalized.debugCaptureRawHtml) {
            rawHtmlDebugPath = await captureRawHtml(page, finalUrl);
        }
        return {
            requestedUrl: normalized.url,
            finalUrl,
            title,
            description,
            openGraph,
            visibleText,
            robotsAllowed: true,
            httpStatus: status,
            contentType,
            contentHash: `sha256:${contentHash}`,
            rawHtmlDebugPath,
            exclusionReason: null
        };
    }
    finally {
        await browser?.close().catch(() => undefined);
    }
}
function createExclusionResponse(requestedUrl, finalUrl, exclusionReason, httpStatus, rawHtmlDebugPath) {
    const contentHash = createHash('sha256').update(`${requestedUrl}\n${exclusionReason}`).digest('hex');
    return {
        requestedUrl,
        finalUrl,
        title: null,
        description: null,
        openGraph: {},
        visibleText: '',
        robotsAllowed: exclusionReason === 'robots-txt' ? false : true,
        httpStatus,
        contentType: null,
        contentHash: `sha256:${contentHash}`,
        rawHtmlDebugPath,
        exclusionReason
    };
}
function detectExcludedUrl(parsedUrl) {
    const path = parsedUrl.pathname.toLowerCase();
    const pathExtension = extractFileExtension(path);
    if (pathExtension === '.pdf') {
        return null;
    }
    const disallowedDownloadExtensions = [
        '.zip', '.gz', '.tar', '.tgz', '.rar', '.7z', '.exe', '.msi', '.deb', '.rpm', '.apk', '.dmg', '.iso',
        '.mp4', '.avi', '.mov', '.mpg', '.mpeg', '.mp3', '.wav', '.flac', '.ogg', '.m4a', '.json', '.xml', '.csv', '.txt',
        '.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.ico', '.svg'
    ];
    if (pathExtension && disallowedDownloadExtensions.includes(pathExtension)) {
        return 'non-pdf-file-download';
    }
    for (const value of parsedUrl.searchParams.values()) {
        const queryExtension = extractFileExtension(value.toLowerCase());
        if (queryExtension && disallowedDownloadExtensions.includes(queryExtension)) {
            return 'non-pdf-file-download';
        }
    }
    const queryText = parsedUrl.search.toLowerCase();
    if (disallowedDownloadExtensions.some((extension) => queryText.includes(extension))) {
        return 'non-pdf-file-download';
    }
    return null;
}
function detectRedirectExclusion(requestedUrl, finalUrl) {
    if (requestedUrl === finalUrl) {
        return null;
    }
    const requested = new URL(requestedUrl);
    const resolved = new URL(finalUrl);
    const trackingParameters = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content', 'gclid', 'fbclid', 'mc_cid', 'mc_eid', 'twclid', 'ref', 'referrer', 'source', 'cid'];
    const hasTrackingParams = trackingParameters.some((parameter) => resolved.searchParams.has(parameter) || requested.searchParams.has(parameter));
    if (hasTrackingParams) {
        return 'tracking-redirect';
    }
    return null;
}
async function detectLoginPage(page, finalUrl) {
    const path = new URL(finalUrl).pathname.toLowerCase();
    if (/(^|\/)(login|signin|sign-in|auth|account|password|register)(\/|$)/.test(path)) {
        return 'login-page';
    }
    const hasPasswordField = await page.locator('input[type="password"]').count().then((count) => count > 0).catch(() => false);
    const bodyText = await page.evaluate(() => document.body?.innerText ?? '').catch(() => '');
    const hasLoginPrompt = /login|sign in|sign-in/i.test(bodyText);
    if (hasPasswordField || hasLoginPrompt) {
        return 'login-page';
    }
    return null;
}
async function extractVisibleText(page) {
    const visibleText = await page.locator('body').innerText().catch(() => '');
    return visibleText.replace(/\s+/g, ' ').trim().slice(0, MAX_VISIBLE_TEXT_LENGTH);
}
async function enforcePerHostRateLimit(hostname, rateLimitDelayMs) {
    const normalizedDelayMs = Math.max(0, Math.floor(rateLimitDelayMs));
    if (normalizedDelayMs === 0) {
        return;
    }
    const normalizedHostname = hostname.toLowerCase();
    const lastRequestAt = HOST_RATE_LIMITS.get(normalizedHostname) ?? 0;
    const now = Date.now();
    const waitMs = Math.max(0, lastRequestAt + normalizedDelayMs - now);
    if (waitMs > 0) {
        await new Promise((resolve) => setTimeout(resolve, waitMs));
    }
    HOST_RATE_LIMITS.set(normalizedHostname, Date.now());
}
async function loadRobotsRules(parsedUrl) {
    const robotsUrl = new URL('/robots.txt', parsedUrl);
    try {
        const response = await fetch(robotsUrl, {
            redirect: 'manual',
            headers: { 'user-agent': SCRAPER_USER_AGENT }
        });
        if (!response.ok) {
            return null;
        }
        const body = await response.text();
        return parseRobotsTxt(body);
    }
    catch {
        return null;
    }
}
function parseRobotsTxt(body) {
    const rules = [];
    let currentRule = null;
    for (const line of body.split(/\r?\n/)) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith('#')) {
            continue;
        }
        const separatorIndex = trimmed.indexOf(':');
        if (separatorIndex === -1) {
            continue;
        }
        const directive = trimmed.slice(0, separatorIndex).trim().toLowerCase();
        const value = trimmed.slice(separatorIndex + 1).trim();
        if (directive === 'user-agent') {
            currentRule = { userAgent: value.toLowerCase(), disallow: [], allow: [] };
            rules.push(currentRule);
            continue;
        }
        if (!currentRule) {
            continue;
        }
        if (directive === 'disallow') {
            currentRule.disallow.push(value);
        }
        else if (directive === 'allow') {
            currentRule.allow.push(value);
        }
    }
    return rules;
}
function isRobotsAllowed(pathname, rules) {
    if (!rules || rules.length === 0) {
        return true;
    }
    const normalizedPath = pathname.startsWith('/') ? pathname : `/${pathname}`;
    const wildcardRules = rules.filter((rule) => rule.userAgent === '*' || rule.userAgent === 'playwright' || rule.userAgent === 'chrome' || rule.userAgent === 'mozilla');
    if (wildcardRules.length === 0) {
        return true;
    }
    for (const rule of wildcardRules) {
        const disallowed = rule.disallow.some((pattern) => pathMatchesRule(normalizedPath, pattern));
        if (disallowed) {
            const allowed = rule.allow.some((pattern) => pathMatchesRule(normalizedPath, pattern));
            if (!allowed) {
                return false;
            }
        }
    }
    return true;
}
function pathMatchesRule(pathname, rulePath) {
    if (!rulePath) {
        return false;
    }
    const normalizedRule = rulePath.startsWith('/') ? rulePath : `/${rulePath}`;
    if (normalizedRule === '/') {
        return true;
    }
    return pathname === normalizedRule || pathname.startsWith(normalizedRule);
}
function extractFileExtension(value) {
    const match = value.match(/\.[a-z0-9]{2,5}(?:$|[?#&])/i);
    return match ? match[0].replace(/[?#&]$/, '').toLowerCase() : null;
}
async function captureRawHtml(page, finalUrl) {
    const html = await page.content();
    const debugDirectory = join(tmpdir(), 'streaming-digest-scraper');
    await fs.mkdir(debugDirectory, { recursive: true });
    const fileName = `${Date.now()}-${createHash('sha256').update(finalUrl).digest('hex')}.html`;
    const filePath = join(debugDirectory, fileName);
    await fs.writeFile(filePath, html, 'utf8');
    return filePath;
}
