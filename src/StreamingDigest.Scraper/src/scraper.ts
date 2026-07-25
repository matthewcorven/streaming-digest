import { createHash } from 'node:crypto';
import { chromium } from 'playwright';

export interface ScrapeFirstPageRequest {
  url: string;
  respectRobotsTxt: boolean;
  debugCaptureRawHtml: boolean;
  timeoutSeconds: number;
}

export interface ScrapeFirstPageResponse {
  finalUrl: string;
  title: string | null;
  description: string | null;
  openGraph: Record<string, string>;
  visibleText: string;
  robotsAllowed: boolean;
  httpStatus: number;
  contentType: string | null;
  contentHash: string;
  rawHtmlDebugPath: string | null;
}

export function normalizeScrapeRequest(input: Partial<ScrapeFirstPageRequest>): ScrapeFirstPageRequest {
  if (!input.url || typeof input.url !== 'string') {
    throw new Error('A non-empty url is required.');
  }

  let parsedUrl: URL;
  try {
    parsedUrl = new URL(input.url);
  } catch {
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
    timeoutSeconds
  };
}

export async function scrapeFirstPage(request: Partial<ScrapeFirstPageRequest>): Promise<ScrapeFirstPageResponse> {
  const normalized = normalizeScrapeRequest(request);

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const timeoutMs = normalized.timeoutSeconds * 1000;
  page.setDefaultTimeout(timeoutMs);

  const response = await page.goto(normalized.url, { waitUntil: 'domcontentloaded' });
  const finalUrl = response?.url() ?? normalized.url;
  const title = await page.title().catch(() => null);
  const description = await page.locator('meta[name="description"]').getAttribute('content').catch(() => null);
  const visibleText = (await page.locator('body').innerText()).trim();
  const openGraph = await page.evaluate(() => {
    const values: Record<string, string> = {};
    for (const element of Array.from(document.querySelectorAll('meta[property^="og:"]'))) {
      const property = element.getAttribute('property');
      const content = element.getAttribute('content');
      if (property && content) {
        values[property] = content;
      }
    }
    return values;
  });

  const contentHash = createHash('sha256').update(`${title ?? ''}\n${visibleText}`).digest('hex');
  const status = response?.status() ?? 0;

  await browser.close();

  return {
    finalUrl,
    title,
    description,
    openGraph,
    visibleText,
    robotsAllowed: normalized.respectRobotsTxt ? true : true,
    httpStatus: status,
    contentType: response?.headers()['content-type'] ?? null,
    contentHash: `sha256:${contentHash}`,
    rawHtmlDebugPath: null
  };
}
