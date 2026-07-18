# Project Context

- **Project:** streaming-digest
- **Created:** 2026-07-17
- **Requested by:** Matthew Corven
- **Stack:** ASP.NET Core 10 API, hosted Blazor WASM without SSR, PostgreSQL + pgvector, Docker Compose, Aspire orchestration, local AI services

## Core Context

Streaming Digest is a self-hosted YouTube knowledge ingestion, search, curation, and observability platform.

## Recent Updates

📌 Team initialized on 2026-07-18
📌 2026-07-18: Reviewed Trinity's Fluent UI declaration — approved as consistent across ARCHITECTURE.md, DATA_MODEL.md, IMPLEMENTATION_PLAN.md, PRD.md. Flagged missing Fluent UI package in Task 0.2; coordinator closed the gap.
📌 2026-07-18: Reviewed Trinity's PWA-first declaration — approved as consistent across ARCHITECTURE.md, PRD.md, IMPLEMENTATION_PLAN.md, UPGRADE_PATHS.md. Flagged 2 follow-ups (PWA task items in implementation plan; service-worker upgrade edge case in upgrade paths); coordinator closed both via direct edits (Task 0.2 bullet + Task 2.3c; UPGRADE_PATHS §3.1 + §3.3).

## Learnings

Morpheus owns architecture, scope, and review across the squad.

- 2026-07-18: Reviewed Trinity's Fluent UI declaration in ARCHITECTURE.md (§2.1, §5.2). UI = Blazor WASM, no SSR, static files from API, Fluent UI Blazor components (`Microsoft.FluentUI.AspNetCore.Components`), HTTP-only client↔server. Verdict: consistent across ARCHITECTURE.md, DATA_MODEL.md, IMPLEMENTATION_PLAN.md, PRD.md. One minor follow-up flagged: IMPLEMENTATION_PLAN.md Task 0.2 package list doesn't mention the Fluent UI NuGet package (not a contradiction, but worth adding when implementation starts). DATA_MODEL.md correctly makes no UI-tech claims. `fluentui-blazor` skill referenced in ARCHITECTURE.md §5.2 for dev-time guidance.
- 2026-07-18: Reviewed Trinity's PWA declaration in ARCHITECTURE.md (§1 goals, §2.1, §5.2 **PWA:** block). PWA embraced from start of UI implementation: manifest, installability, icons, service worker for lifecycle/install only, responsive UX; full offline mode explicitly MVP+. Verdict: ✅ consistent — PWA + no-SSR WASM + static hosting is a natural fit; HTTPS requirement satisfied by Tailscale/reverse-proxy + localhost dev. PRD.md needs no changes (mobile-friendly secondary UX aligns; "Mobile-native application" non-goal actually reinforces PWA). Flagged follow-ups: IMPLEMENTATION_PLAN.md Task 0.2 has no PWA asset items (manifest/service-worker), Task 2.3 app shell has no explicit PWA task (suggest Task 2.3c: manifest, icons, service-worker registration, install verification); UPGRADE_PATHS.md should eventually cover service-worker update prompt / WASM asset cache-busting on app upgrade (§3.1 "Hosted WASM assets should be version-aligned with API" is the hook). Trinity owns the ARCHITECTURE.md edit; implementation plan and upgrade-paths edits are follow-ups, not blockers.
