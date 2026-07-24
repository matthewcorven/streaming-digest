### Task 10.1a: Establish scraper service toolchain and orchestration

If the scraper is Node/TypeScript (per Task 0.1), `dotnet build` alone does not cover it.

Requirements:

- Node/TypeScript build and test commands (`npm ci`, `npm run build`, `npm test`) for the scraper service.
- Playwright browser installation as a documented, repeatable build/Docker step.
- Aspire AppHost orchestration of the non-.NET scraper service in local development.
- Compose service wiring for the scraper, including internal-only networking and health checks.
- Typed worker scraper client matching the internal scraper API contract (`docs/api/API_SPEC.md` §20), including health-check integration.

Verification:

- Scraper build/test commands pass locally and in CI.
- Aspire AppHost starts the scraper alongside .NET services.
- Compose stack starts the scraper with the API/worker able to reach it on the internal network.

