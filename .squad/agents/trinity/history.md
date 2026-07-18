# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest exposes a search-first and admin-oriented Blazor WASM interface.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-07-18: Declared Microsoft Fluent UI Blazor as the UI component library in ARCHITECTURE.md (header, §2.1, §5.2). Morpheus reviewed and confirmed cross-doc consistency. Coordinator added the Fluent UI package to IMPLEMENTATION_PLAN.md Task 0.2 baseline.
📌 2026-07-18: Declared PWA-first in ARCHITECTURE.md (header, §1 goals, §2.1, new §5.2 PWA subsection). Morpheus approved as consistent across PRD/IMPLEMENTATION_PLAN/UPGRADE_PATHS. Coordinator closed follow-ups: IMPLEMENTATION_PLAN.md Task 0.2 PWA assets bullet + new Task 2.3c "Establish PWA baseline"; UPGRADE_PATHS.md §3.1 service worker update flow + §3.3 stale WASM assets edge case.

## Learnings

Frontend work should assume no SSR and should support desktop and mobile admin use.
- UI component library: Microsoft Fluent UI Blazor (`Microsoft.FluentUI.AspNetCore.Components`). No other CSS framework needed.
- The `fluentui-blazor` skill is available for Fluent UI component patterns, theming, and integration guidance.
- Architecture doc updated to declare Fluent UI + no-SSR in header, section 2.1, and section 5.2.
- 2026-07-18: Declared PWA-first in ARCHITECTURE.md (target runtime line, §1 goals, §2.1 bullet, §5.2 PWA subsection). PWA is embraced from the very start of UI implementation — manifest, installability, icons/splash, service worker registration (lifecycle/install only), responsive mobile-first UX are MVP scope.
- Full offline mode (offline data caching, offline search, background sync of user actions) is explicitly deferred to MVP+; the MVP service worker foundation makes it incremental later.
- The `pwa-development` skill is the authoritative reference for PWA patterns — prefer it over assumptions. https://whatpwacando.today/ is the per-platform capability reference.
- PWA is compatible with the no-SSR static hosting model: WASM project publishes service-worker.js + manifest.json as static assets served by the API.
