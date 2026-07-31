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
📌 2026-07-18: Authored docs/presentation/PRESENTATION.md from an interactive design session with Matthew Corven. Morpheus reviewed and APPROVED WITH NOTES — one fix applied in place (§3.3 post-login routing precedence now includes PRD §2.10's "last selected mode" step). The doc is now the authoritative presentation layer reference.
📌 2026-07-18 (team-wide): Decisions are now CLASSIFIED before inbox write. ARCHITECTURAL decisions (hard to reverse, real trade-off, future reader needs the "why") → full ADR at `docs/adr/NNNN-<slug>.md` (format per `.agents/skills/domain-modeling/ADR-FORMAT.md`, next unused NNNN) + one-line pointer in the inbox. TEAM/PROCESS/SCOPE decisions → full text in the inbox as today. `.squad/decisions.md` is the single index of ALL decisions; architectural rationale lives exactly once, in the ADR. Read the Decision Classification block in squad.agent.md (Drop-Box Pattern section) when you next write a decision. Applies going forward — existing ADRs 0001–0010 and decisions.md entries untouched.

## Learnings

Frontend work should assume no SSR and should support desktop and mobile admin use.
- UI component library: Microsoft Fluent UI Blazor (`Microsoft.FluentUI.AspNetCore.Components`). No other CSS framework needed.
- The `fluentui-blazor` skill is available for Fluent UI component patterns, theming, and integration guidance.
- Architecture doc updated to declare Fluent UI + no-SSR in header, section 2.1, and section 5.2.
- 2026-07-18: Declared PWA-first in ARCHITECTURE.md (target runtime line, §1 goals, §2.1 bullet, §5.2 PWA subsection). PWA is embraced from the very start of UI implementation — manifest, installability, icons/splash, service worker registration (lifecycle/install only), responsive mobile-first UX are MVP scope.
- Full offline mode (offline data caching, offline search, background sync of user actions) is explicitly deferred to MVP+; the MVP service worker foundation makes it incremental later.
- The `pwa-development` skill is the authoritative reference for PWA patterns — prefer it over assumptions. https://whatpwacando.today/ is the per-platform capability reference.
- PWA is compatible with the no-SSR static hosting model: WASM project publishes service-worker.js + manifest.json as static assets served by the API.
- 2026-07-18: Presentation layer doc created at docs/presentation/PRESENTATION.md — definitive UI spec covering app shell, all 8 pages, 6 components, interaction patterns, empty/error states, PWA specifics, with ASCII wireframes throughout.
- Key decisions: SSE over polling, bento dashboard, inline accordion expansion, hybrid onboarding (wizard + checklist), 5-tab edit modal, modal-only notes (EasyMDE), full keyboard shortcuts.
- Responsive strategy: Fluent UI default breakpoints (xs/sm/md/lg/xl), mobile-first CSS, desktop-primary usage. Desktop = sidebar nav + bento dashboard + filter sidebar; mobile = hamburger drawer + single-column + bottom-sheet filters.
- SSE four streams: ingestion run progress, pending-action inbox changes, operation status, digest availability. Fallback to 30s polling after 3 failed reconnects; re-fetch on reconnect to close event gaps.
- Result card contract: collapsed = thumbnail + title/channel/date/duration/match-count/primary-snippet/type+score/action links (per PRD §2.5 + API_SPEC §8 video-cluster shape); expanded = submatches, related items w/ relative %, screenshot gallery, timestamps, score breakdown, warnings, Edit/Notes.
- Search state lives in the URL (query + filters + weights + page) for bookmarking/refresh/history.

## 2026-07-30 — Issue #30 Dashboard Digest

✅ **Session 6acd1378-ea22-4bb4-ba22-fbb3ca848601 COMPLETED**

**Branch:** matthewcorven-issue-30-dashboard-digest-inbox  
**Commit:** 191d4e6  
**Closed on GitHub:** Issue #30 — "Implemented in this commit."

**Implementation Summary:**

- Reordered dashboard to: daily digest → search launchpad → pending-action inbox
- Added fixture-backed dashboard summary service/models for digest sections
- Implemented live deferment tracking
- High-signal recent-search matches with relative-similarity percentages, timestamp/repository/website links
- Ordered pending-action rows with Retry/Approve/Test actions
- Updated login routing preference order
- Search honors ?q= launchpad routing
- Search waits for completed corpus before enabling
- Focused unit tests: digest ordering, links, actions, route resolution, session mode tracking

**Status:** ✅ Complete — Issue closed on GitHub.
