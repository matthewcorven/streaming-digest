### Task 0.1: Create solution structure

Create projects:

- `src/StreamingDigest.AppHost` - Aspire AppHost.
- `src/StreamingDigest.Api` - ASP.NET Core API + Blazor WASM host.
- `src/StreamingDigest.Web` - Blazor WASM client if using hosted template split.
- `src/StreamingDigest.Worker` - Hangfire worker.
- `src/StreamingDigest.Domain` - domain entities/value objects.
- `src/StreamingDigest.Application` - use cases/services/contracts.
- `src/StreamingDigest.Infrastructure` - EF Core, Dapper, external adapters.
- `src/StreamingDigest.MatrixNotifier` - Matrix notification service; E2EE is MVP+.
- `src/StreamingDigest.Scraper` - Crawlee/Playwright scraper service, likely Node/TypeScript.
- `tests/StreamingDigest.UnitTests`
- `tests/StreamingDigest.IntegrationTests`

Verification:

- `dotnet build` succeeds.
- Aspire AppHost starts local dependencies.

