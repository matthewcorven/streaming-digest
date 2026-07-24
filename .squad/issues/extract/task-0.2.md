### Task 0.2: Add baseline package dependencies

Add packages for:

- ASP.NET Core 10 LTS.
- Blazor WASM hosted setup (no SSR — client served as static files from the API project).
- Microsoft Fluent UI Blazor components (`Microsoft.FluentUI.AspNetCore.Components` NuGet package) — the UI design system; see `docs/architecture/ARCHITECTURE.md` §5.2 and the fluentui-blazor skill for usage guidance.
- PWA baseline assets (web app manifest, app icons/maskable icons, service worker registration) — scaffolded in the WASM project from the start; see `docs/architecture/ARCHITECTURE.md` §5.2 PWA subsection, the pwa-development skill, and https://whatpwacando.today/. Full offline mode is MVP+.
- Aspire orchestration.
- Npgsql EF Core provider.
- pgvector Npgsql support.
- Dapper.
- Hangfire.AspNetCore and Hangfire.PostgreSql.
- OpenTelemetry.
- Serilog or Microsoft.Extensions.Logging structured logging.
- Argon2id password hashing library.
- Microsoft Semantic Kernel.
- EasyMDE frontend package.

Verification:

- Restore succeeds.
- No package downgrade warnings.

