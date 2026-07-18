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

## Learnings

Frontend work should assume no SSR and should support desktop and mobile admin use.
- UI component library: Microsoft Fluent UI Blazor (`Microsoft.FluentUI.AspNetCore.Components`). No other CSS framework needed.
- The `fluentui-blazor` skill is available for Fluent UI component patterns, theming, and integration guidance.
- Architecture doc updated to declare Fluent UI + no-SSR in header, section 2.1, and section 5.2.
