# Squad Decisions

## Active Decisions

- 2026-07-18: Initial squad roster set to Morpheus, Trinity, Tank, Dozer, Neo, Switch, Scribe, and Ralph.
- 2026-07-18: Initial casting universe set to The Matrix for this repository.
- 2026-07-18: Frontend scope is hosted Blazor WASM without server-side rendering.
- 2026-07-18: UI component library is Microsoft Fluent UI Blazor (`Microsoft.FluentUI.AspNetCore.Components` NuGet package). Blazor WASM without SSR, served as static files from the ASP.NET Core 10 API project, communicating exclusively via HTTP API calls. No other CSS framework needed. The `fluentui-blazor` skill is available for development reference. (By: Trinity, via Matthew Corven — user directive)
- 2026-07-18: Streaming Digest's Blazor WASM UI is a Progressive Web App from day one of UI implementation, not retrofitted. MVP scope: web app manifest, installability, app icons/splash (incl. maskable), service worker registration for lifecycle/install (not offline), responsive mobile-first layout, and platform-appropriate UX using PWA capabilities (display-mode detection, share target, shortcuts) as needed. Full offline mode — offline data caching, offline search, background sync of user actions — is explicitly MVP+, with the MVP service worker foundation laid so it's an incremental addition. The `pwa-development` skill is the authoritative reference for PWA patterns (preferred over assumptions); https://whatpwacando.today/ is the per-platform capability reference. Declared in ARCHITECTURE.md (target runtime line, §1 goals, §2.1, §5.2 PWA subsection); compatible with the no-SSR static hosting model since the WASM project publishes service-worker.js and manifest.json as static assets served by the API. (By: Trinity, via Matthew Corven — user request)
- 2026-07-18: Trinity's PWA-first UI declaration in ARCHITECTURE.md (§1, §2.1, §5.2) reviewed and approved as architecturally consistent with existing Fluent UI / no-SSR WASM / static hosting declarations and PRD.md. Follow-ups closed by coordinator: PWA task items added to IMPLEMENTATION_PLAN.md (Task 0.2 baseline assets bullet, new Task 2.3c "Establish PWA baseline"); service-worker update flow + stale WASM asset edge case added to UPGRADE_PATHS.md (§3.1, §3.3). (By: Morpheus — review verdict)

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
