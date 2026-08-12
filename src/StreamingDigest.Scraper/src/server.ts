import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { scrapeFirstPage, type ScrapeFirstPageRequest } from './scraper.js';

export function createScraperServer() {
  return createServer(async (request: IncomingMessage, response: ServerResponse) => {
    console.log(`[DEBUG] Received ${request.method} ${request.url}`);
    if ((request.method === 'GET' || request.method === 'HEAD') && request.url === '/health') {
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ status: 'ok' }));
      return;
    }

    if (request.method === 'POST' && request.url === '/internal/scrape/first-page') {
      const body = await readJsonBody(request);
      try {
        const requestBody = body as Partial<ScrapeFirstPageRequest>;
        const payload = await scrapeFirstPage(requestBody);
        response.writeHead(200, { 'content-type': 'application/json' });
        response.end(JSON.stringify(payload));
      } catch (error) {
        response.writeHead(400, { 'content-type': 'application/json' });
        response.end(JSON.stringify({ error: error instanceof Error ? error.message : 'Unknown error' }));
      }
      return;
    }

    response.writeHead(404, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ error: 'Not found' }));
  });
}

export function startScraperServer(port = Number(process.env.PORT ?? '3000')) {
  const server = createScraperServer();
  server.listen(port, () => {
    console.log(`Scraper service listening on port ${port}`);
  });
  return server;
}

async function readJsonBody(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  const rawBody = Buffer.concat(chunks).toString('utf8');
  if (!rawBody.trim()) {
    return {};
  }

  return JSON.parse(rawBody);
}
