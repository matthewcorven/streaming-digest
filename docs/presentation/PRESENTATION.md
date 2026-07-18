# Streaming Digest Presentation Layer

Status: MVP presentation design agreed
Framework: Blazor WASM (no SSR), Microsoft Fluent UI Blazor, PWA-enabled

Source documents:

- `docs/product/PRD.md` — product scope, user journeys, acceptance criteria
- `docs/architecture/ARCHITECTURE.md` — hosting model, PWA declaration, search UX conventions (§2.1, §4.10, §5.2)
- `docs/api/API_SPEC.md` — all endpoint contracts referenced below
- `docs/implementation/IMPLEMENTATION_PLAN.md` — Task 2.3 (app shell), Task 2.3a (run detail UI), Task 2.3b (conformance harness), Task 2.3c (PWA baseline)

This document is the definitive, implementable specification for the Streaming Digest UI. Every page, component, interaction pattern, and state is specified concretely. Where a behavior is not specified here, the source documents above win.

---

## 1. Design Principles

1. **Search is the product.** Every layout decision optimizes the search journey: query input → clustered results → expand → open artifact (timestamp, repo, website, note). All other surfaces support this loop or the ingestion pipeline that feeds it.
2. **Desktop-primary, mobile-secondary.** Desktop is the curation/admin workbench. Mobile is the reading surface — digest review, opening links from Matrix notifications. Every page works at mobile widths; only the dashboard and search are expected to be used heavily on mobile.
3. **Fluent UI Blazor is the component system.** All controls come from `Microsoft.FluentUI.AspNetCore.Components`. No other CSS framework. Custom CSS is limited to layout (grid/flex), spacing, and component-local styling via CSS isolation.
4. **No SSR, no hidden state.** The app is 100% client-side Blazor WASM. UI state is explicit, inspectable, and driven by HTTP API calls. No SignalR circuits. Server push is via SSE only (§6.1).
5. **Real-time by push, not poll.** Live state (ingestion progress, inbox, operations, digest) arrives over Server-Sent Events. Polling exists only as a fallback when SSE drops (§6.1).
6. **Incomplete data is visible, not hidden.** Partially processed videos appear in search with warning badges (PRD §2.5, ARCHITECTURE §4.10). Degraded states are first-class.
7. **Every empty state is actionable.** An empty page always tells the user what to do next and provides the CTA to do it (§7).
8. **Keyboard parity.** Every primary action on the search journey is reachable by keyboard (§6.2).
9. **URL is the shareable state.** Search query and filters live in the URL so any search can be bookmarked, refreshed, or shared (§6.3).
10. **PWA from day one.** Installable, standalone display, app shortcuts, service worker lifecycle — per ARCHITECTURE §5.2. Full offline mode is MVP+ (§8).

---

## 2. Responsive Breakpoints & Layout Strategy

### 2.1 Breakpoints

Fluent UI Blazor default breakpoints are used unchanged:

| Token | Range | Role in Streaming Digest |
|---|---|---|
| `xs` | < 640px | Phone portrait. Single-column everywhere. |
| `sm` | 640–767px | Phone landscape / small tablet. Single-column. |
| `md` | 768–1023px | Tablet. Stacked dashboard, filter drawer on search. |
| `lg` | 1024–1279px | Small desktop. Sidebar nav visible, bento dashboard. |
| `xl` | ≥ 1280px | Full desktop. Bento dashboard, filter sidebar on search. |

Layout logic uses CSS media queries and Fluent UI's grid utilities. Component visibility switches (e.g., sidebar vs. bottom sheet) are driven by these breakpoints.

Three functional layout classes derive from the tokens:

- **Mobile** = `xs` + `sm` (< 768px): hamburger nav, single-column stacks, bottom sheets, full-width cards.
- **Tablet** = `md` (768–1023px): sidebar nav (collapsible), stacked dashboard, bottom-sheet filters on search.
- **Desktop** = `lg` + `xl` (≥ 1024px): persistent sidebar nav, bento dashboard grid, filter sidebar on search, multi-column digest cards.

The bento dashboard's 2-column digest span engages at ≥1200px per the confirmed design (between `lg` and `xl`).

### 2.2 Layout strategy

- **Mobile-first CSS.** Base styles target `xs`; media queries layer up.
- **App shell owns the chrome.** Sidebar nav, top bar, user menu, theme — all pages render inside the shell (§3).
- **Page content owns the grid.** Each page defines its own responsive grid within the shell's content area.
- **Containers, not pages, reflow.** Cards, toolbars, and tables reflow internally (e.g., channels table → cards) rather than pages switching layouts wholesale.
- **Touch targets ≥ 44px** on mobile for all interactive elements.

---

## 3. App Shell

The app shell is the persistent chrome around all authenticated pages. It renders after login and never unloads during client-side navigation.

### 3.1 Structure

**Desktop (≥1024px):**

```
┌──────────────────────────────────────────────────────────────────────┐
│ ▓▓ SIDEBAR ▓▓ │  TOP BAR: page title            [avatar ▾]           │
│ ┌───────────┐ ├──────────────────────────────────────────────────────┤
│ │ ◆ Stream  │ │                                                      │
│ │   Digest  │ │                                                      │
│ ├───────────┤ │                                                      │
│ │ 🏠 Dashboard│ │                                                      │
│ │ 🔍 Search  │ │                PAGE CONTENT                          │
│ │ 📺 Channels│ │                                                      │
│ │ ▶ Ingestion│ │                                                      │
│ │ ⚙ Settings │ │                                                      │
│ ├───────────┤ │                                                      │
│ │ ● Ready   │ │                                                      │
│ └───────────┘ │                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

**Mobile (<768px):**

```
┌─────────────────────────────────┐
│ ☰  Page Title          [avatar] │  ← top bar; ☰ opens drawer overlay
├─────────────────────────────────┤
│                                 │
│           PAGE CONTENT          │
│                                 │
└─────────────────────────────────┘

Drawer overlay (on ☰):
┌────────────────┐
│ ◆ Stream Digest│
│ 🏠 Dashboard   │
│ 🔍 Search      │
│ 📺 Channels    │
│ ▶ Ingestion    │
│ ⚙ Settings     │
│ ──────────────│
│ ● Ready        │
└────────────────┘
```

### 3.2 Elements

| Element | Implementation | Behavior |
|---|---|---|
| Sidebar nav | `FluentNavMenu` inside `FluentLayout` | Persistent on desktop; drawer overlay (`FluentDialog`-style overlay) on mobile. Active route highlighted. |
| Nav badges | Custom badge component (§5.6) | Inline count badges on Dashboard (pending inbox) and Ingestion (active runs). Updated via SSE. |
| Top bar | `FluentHeader` | Current page title; avatar menu (logout, theme quick-switch). |
| User menu | `FluentMenu` anchored to avatar | Items: Theme (Light/Dark/System), Logout. |
| Status footer (sidebar bottom) | Custom component | Readiness dot: green (fully ready), amber (core ready, warnings), red (core incomplete). Tooltip lists failing checks from `GET /api/onboarding/status`. Click → Settings. |
| Logo/brand | SVG logo + "Stream Digest" wordmark | Links to `/` (dashboard). |

### 3.3 Routing

| Route | Page | Notes |
|---|---|---|
| `/login` | Login | Outside shell. |
| `/change-password` | Forced password change | Outside shell; separate screen, not a modal. |
| `/onboarding` | Onboarding wizard/checklist | Inside shell but nav locked until core setup complete. |
| `/` or `/dashboard` | Dashboard | Default after login (per PRD §2.10 routing precedence). |
| `/search` | Search | Query/filter state in URL (§6.3). |
| `/channels` | Channels | |
| `/ingestion` | Ingestion Runs list | |
| `/ingestion/runs/{runId}` | Ingestion Run Detail | |
| `/settings` | Admin/Settings | Tabbed; `/settings/{tab}` deep-links to a tab. |

**Post-login routing precedence** (PRD §2.10): incomplete onboarding → forced password change → last selected mode → dashboard (after first daily run) → ingestion digest. The client evaluates this after `GET /api/auth/me` + `GET /api/onboarding/status` on bootstrap.

### 3.4 Bootstrap sequence

1. Load `blazor.webassembly.js`, register `service-worker.js` (§8.2).
2. `GET /api/auth/me` → if 401, route to `/login`.
3. If `mustChangePassword: true` → route to `/change-password`.
4. `GET /api/onboarding/status` → if `isCoreSetupComplete: false` → route to `/onboarding`.
5. Open SSE connection (§6.1).
6. Render shell + default route.

---

## 4. Page Specifications

### 4.1 Login

**Purpose:** Authenticate the single admin user (PRD §2.9). First impression — branded, calm, confident.

**Route:** `/login` (outside app shell)

**Layout:** Centered card on a subtle branded background. No navigation.

```
DESKTOP + MOBILE (same layout, card width adapts: 380px desktop, full-width mobile)

┌─────────────────────────────────────────────┐
│  (subtle gradient/pattern background)       │
│                                             │
│        ┌─────────────────────────┐          │
│        │        ◆ (logo)         │          │
│        │    Stream Digest        │          │
│        │  Your YouTube knowledge │          │
│        │       base              │          │
│        │                         │          │
│        │  Username               │          │
│        │  ┌───────────────────┐  │          │
│        │  │                   │  │          │
│        │  └───────────────────┘  │          │
│        │  Password               │          │
│        │  ┌───────────────────┐  │          │
│        │  │                👁 │  │          │
│        │  └───────────────────┘  │          │
│        │                         │          │
│        │  ┌───────────────────┐  │          │
│        │  │     Sign in       │  │          │
│        │  └───────────────────┘  │          │
│        │                         │          │
│        │  ⚠ error message here   │          │
│        └─────────────────────────┘          │
└─────────────────────────────────────────────┘
```

**Key interactions:**

- Submit on Enter or button click → `POST /api/auth/login`.
- Password visibility toggle (eye icon).
- On success with `mustChangePassword: true` → route to `/change-password`.
- On success otherwise → bootstrap routing precedence (§3.4).
- Rate-limit responses (429 from login endpoint) show a friendly cooldown message.

**Data dependencies:**

- `POST /api/auth/login` — authenticate, sets secure HTTP-only cookie.
- `GET /api/auth/csrf` — fetched immediately after login for mutating endpoints.

**Forced password change** (`/change-password`): separate screen with the same branded shell — current password, new password (with strength hints), confirm new password. Submits to `POST /api/auth/change-password`. On success, routes into the app. It is deliberately a full screen, not a modal, because it is a blocking security gate.

**Empty state:** n/a (form page).

**Error state:** Inline error banner in the card: "Invalid username or password." / "Too many attempts — try again in {n} minutes." Network failure → "Cannot reach the server. Check that Streaming Digest is running."

---

### 4.2 Onboarding

**Purpose:** Guide the user through first-run setup (PRD §2.10). Hybrid: blocking linear wizard for core steps, non-blocking readiness checklist for operational hardening.

**Route:** `/onboarding`

**Layout:**

- **Steps 1–5 (wizard):** Full-width step panel with a progress header. One step visible at a time. Blocking — the app is not usable until core setup completes.
- **Step 6 (checklist):** Non-blocking readiness panel. Reachable from Settings later; each item has live verify, inline retry, retained values.

**Wizard steps:**

1. **Change admin password** (skipped if already changed during login flow).
2. **Verify embedding model** — shows configured model (`bge-m3` preferred), download option, live verify.
3. **Verify local LLM** — same pattern.
4. **Add first channel** — YouTube URL/handle/ID input with validation.
5. **Confirm ingestion schedule** — default 6:00 AM local time, editable.

**Checklist items (Step 6):**

6. **Whisper/audio-to-text** — warning state allowed; captioned ingestion may proceed (PRD §2.10).
7. **Matrix notifications** — room ID, test send.
8. **Grafana/observability** — endpoint verification.

```
WIZARD (steps 1–5) — DESKTOP
┌──────────────────────────────────────────────────────────────┐
│ ◆ Stream Digest — First-run setup                            │
│                                                              │
│  ●────●────●────○────○    Step 3 of 5                        │
│  Pass  Embed  LLM  Chan  Sched                               │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  Verify local LLM                                      │  │
│  │                                                        │  │
│  │  Streaming Digest uses a local LLM for link            │  │
│  │  classification and segment titles.                    │  │
│  │                                                        │  │
│  │  Model:  [ llama3.1:8b          ▾ ]                    │  │
│  │                                                        │  │
│  │  [ ⬇ Download model ]   [ ✓ Verify now ]               │  │
│  │                                                        │  │
│  │  Status: ✔ Verified — llama3.1:8b responding (182ms)   │  │
│  │  ─ or ─                                                │  │
│  │  Status: ✖ Ollama model not available.                 │  │
│  │          [ Retry ]  [ View setup hints ]               │  │
│  │                                                        │  │
│  │                          [ Back ]      [ Continue → ]  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘

WIZARD — MOBILE
┌─────────────────────────────┐
│ ◆ Setup — Step 3 of 5       │
│ ●●●○○                       │
│ ┌─────────────────────────┐ │
│ │ Verify local LLM        │ │
│ │ Model: [ llama3.1:8b ▾] │ │
│ │ [ ⬇ Download ]          │ │
│ │ [ ✓ Verify now ]        │ │
│ │ Status: ✔ Verified      │ │
│ │                         │ │
│ │ [ Back ]  [ Continue → ]│ │
│ └─────────────────────────┘ │
└─────────────────────────────┘

CHECKLIST (step 6) — DESKTOP
┌──────────────────────────────────────────────────────────────┐
│ Readiness checklist                                          │
│ Core setup complete ✔ — these steps unlock full operations.  │
│                                                              │
│ ┌────────────────────────────────────────────────────────┐   │
│ │ ✖ Whisper audio-to-text           [ Verify ] [ Retry ] │   │
│ │   Needed for videos without captions.                  │   │
│ │   ⚠ Ingestion can proceed for captioned videos.        │   │
│ ├────────────────────────────────────────────────────────┤   │
│ │ ✖ Matrix notifications              [ Configure ]      │   │
│ │   Room ID: [ !room:matrix.org        ] [ Send test ]   │   │
│ ├────────────────────────────────────────────────────────┤   │
│ │ ✔ Observability endpoints           Verified 10:02 AM  │   │
│ └────────────────────────────────────────────────────────┘   │
│                        [ Finish — go to Dashboard ]          │
└──────────────────────────────────────────────────────────────┘
```

**Key interactions:**

- Each wizard step: **live verification** (`POST /api/onboarding/steps/{stepKey}/verify`), **inline retry**, **retained values** (entered values persist across steps and refreshes), clear success/failure messaging (PRD §2.10).
- Continue is disabled until the step's check succeeds.
- Checklist items are independently verifiable/retryable and non-blocking.
- Finish → `POST /api/onboarding/complete-core-setup` (enabled only when core checks pass) → route to dashboard.

**Data dependencies:**

- `GET /api/onboarding/status` — step list, statuses, error summaries.
- `POST /api/onboarding/steps/{stepKey}/verify` — run one check.
- `POST /api/onboarding/complete-core-setup` — mark core setup done.
- `GET /api/models/options`, `POST /api/models/download`, `POST /api/models/verify` — model steps.
- `POST /api/channels` — first channel step.
- `PUT /api/settings` — schedule confirmation.
- `POST /api/admin/test-matrix` — checklist Matrix test.

**Empty state:** n/a — onboarding is the entry state.

**Error state:** Per-step inline failure with `errorSummary` from the status payload and an actionable hint (e.g., "Ollama model bge-m3 not available → Download it here"). A failed core step blocks Continue; a failed checklist item shows amber warning, never a hard stop.

---

### 4.3 Dashboard

**Purpose:** Daily landing surface (PRD §2.1, §2.7; ARCHITECTURE §4.10). Priority order: **daily digest → search launchpad → pending-action inbox**.

**Route:** `/` (alias `/dashboard`)

**Layout:** Responsive bento grid.

**Desktop (≥1200px):** Daily digest spans 2 columns (left); Search launchpad top-right; Pending inbox bottom-right.

```
┌────────────────────────────────────────────────────────────────────┐
│ Dashboard                                                          │
├──────────────────────────────────────────┬─────────────────────────┤
│                                          │ 🔍 SEARCH LAUNCHPAD     │
│  📰 DAILY DIGEST            2026-07-18   │ ┌─────────────────────┐ │
│  ┌────────────────┐ ┌────────────────┐   │ │ Search your videos… │ │
│  │ [thumb]        │ │ [thumb]        │   │ └─────────────────────┘ │
│  │ Video title    │ │ Video title    │   │ Recent:               │
│  │ Channel · 24m  │ │ Channel · 18m  │   │  • github idea search │
│  │ ⭐ 92% match    │ │ 🆕 new         │   │  • postgres vector    │
│  └────────────────┘ └────────────────┘   │  • llm classification │
│  ┌────────────────┐ ┌────────────────┐   ├─────────────────────────┤
│  │ [thumb]        │ │ [thumb]        │   │ 📥 PENDING INBOX    3 │
│  │ Video title    │ │ Video title    │   │ ⚠ 1 failed ingestion  │
│  │ Channel · 31m  │ │ Channel · 12m  │   │ ⏸ 1 rate-limit defer  │
│  │ 🔗 repo         │ │ ⚠ warnings     │   │ 🔄 1 stale embeddings │
│  └────────────────┘ └────────────────┘   │ [ Review all → ]      │
│  [ View full digest → ]                  │                         │
├──────────────────────────────────────────┴─────────────────────────┤
```

**Tablet (768–1199px):** Search launchpad top, Daily digest middle (2-col cards), Pending inbox bottom.

```
┌──────────────────────────────────────────┐
│ 🔍 SEARCH LAUNCHPAD                      │
│ ┌──────────────────────────────────────┐ │
│ │ Search your videos…                  │ │
│ └──────────────────────────────────────┘ │
│ Recent: • query one  • query two         │
├──────────────────────────────────────────┤
│ 📰 DAILY DIGEST                          │
│ ┌──────────────┐ ┌──────────────┐        │
│ │ card         │ │ card         │        │
│ └──────────────┘ └──────────────┘        │
├──────────────────────────────────────────┤
│ 📥 PENDING INBOX                       3 │
│ ⚠ 1 failed ingestion  ⏸ 1 deferment      │
└──────────────────────────────────────────┘
```

**Mobile (<768px):** Single column stack — Search launchpad, Daily digest (single-col cards), Pending inbox.

```
┌───────────────────────┐
│ 🔍 SEARCH LAUNCHPAD   │
│ ┌───────────────────┐ │
│ │ Search…           │ │
│ └───────────────────┘ │
│ Recent: • q1 • q2     │
├───────────────────────┤
│ 📰 DAILY DIGEST       │
│ ┌───────────────────┐ │
│ │ [thumb]           │ │
│ │ Video title       │ │
│ │ Channel · 24m     │ │
│ └───────────────────┘ │
│ ┌───────────────────┐ │
│ │ card              │ │
│ └───────────────────┘ │
├───────────────────────┤
│ 📥 PENDING INBOX    3 │
│ ⚠ 1 failed ingestion  │
│ [ Review all → ]      │
└───────────────────────┘
```

**Widget specifications:**

**Search launchpad:**
- Prominent search input (autofocus on desktop). Submit → routes to `/search?q=...`.
- Recent searches list (max 5): click re-runs that search. Data: `GET /api/recent-searches?pageSize=5`.

**Daily digest:**
- Cards for: new videos ingested, new repositories found, new websites/resources found, items similar to recent searches (with `⭐ {percent}% match` + the matching recent search), failed/skipped items (PRD §2.7).
- Each card: YouTube thumbnail, title, channel, duration, badges (new / match % / warnings / repo link).
- Card click → expands inline (same expansion model as search result card, §5.2) or navigates to search for that video.
- Digest card grid: multi-column on large viewports (fills horizontal space), single column on mobile.
- Live arrival: SSE `digest` stream pushes new digest availability → toast + widget refresh (§6.1).

**Pending inbox:**
- Ordered per ARCHITECTURE §4.10: pending approvals → failed ingestion → degraded channels → deferred rate limits → stale embeddings → model/service warnings → new digest items → recent-search matches → storage/retention warnings.
- Each row: severity icon, label, count, deep link to the resolving page (run detail, settings, channels).
- Badge count on the nav item mirrors inbox item count, updated live via SSE `inbox` stream.

**Key interactions:**

- Digest card action links open YouTube timestamps / repo / website in new tabs and record `POST /api/user-interaction-events`.
- "Run ingestion now" quick action in launchpad header (icon button) → `POST /api/ingestion/run` → toast confirmation.
- Deferment banner renders prominently at top of digest when `GET /api/rate-limit-deferments` has active entries (ARCHITECTURE §4.9).

**Data dependencies:**

- `GET /api/recent-searches` — launchpad recents.
- `GET /api/ingestion/runs?runType=scheduled&pageSize=1` — latest run summary for digest widget source data.
- `GET /api/ingestion/runs/{runId}` — digest detail (new videos, repos, websites, failures).
- `GET /api/rate-limit-deferments` — deferment banner.
- `GET /api/search-documents/stale` — stale-embedding inbox item.
- `GET /api/onboarding/status` — readiness warnings in inbox.
- `POST /api/ingestion/run` — quick action.
- `POST /api/user-interaction-events` — artifact click tracking.
- SSE streams: `inbox`, `digest`, `ingestion` (§6.1).

**Empty state (no ingestion yet):**

```
┌──────────────────────────────────────────┐
│ 📰 Your daily digest will appear here    │
│                                          │
│ Add your first channel and run ingestion │
│ to start building your knowledge base.   │
│                                          │
│ [ ➕ Add a channel ]  [ ▶ Run ingestion ]│
└──────────────────────────────────────────┘
```

Pending inbox empty: "✔ All clear — nothing needs your attention." (No CTA needed; this is the good state.)

**Error state:** Widget-level error card with retry: "Couldn't load the digest. [ Retry ]" — other widgets continue to render. SSE disconnect shows a subtle "reconnecting…" pill in the top bar (§6.1).

---

### 4.4 Search

**Purpose:** The killer journey (PRD §2.1, §2.5, §4.2). Hybrid text+vector search returning video-clustered results with explanations, score components, related items, and direct artifact jumps.

**Route:** `/search` — query and filters in URL (§6.3).

**Layout:** Hybrid — **filter sidebar on desktop**, **bottom sheet/drawer on mobile**. Recent searches appear in both the dashboard launchpad and on the search page.

**Desktop (≥1024px):**

```
┌──────────────────────────────────────────────────────────────────────┐
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ 🔍 code project that searches for project ideas…           [✕]   │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│ Text ⚖ Vector  [────●─────────] 0.5 / 0.5      100 results · 312ms   │
├──────────────┬───────────────────────────────────────────────────────┤
│ FILTERS      │ ┌───────────────────────────────────────────────────┐ │
│ Channel      │ │ [thumb] Video title                          [n][e]│ │
│ ☑ TonbisAI   │ │ Channel · Jul 16 · 24:12 · 12 matches inside      │ │
│ ☐ Other      │ │ "…snippet of primary match…"        segment · 87% │ │
│              │ │ ▶12:34  🔗repo  🌐site  📝note      ⚠ warnings    │ │
│ Date range   │ └───────────────────────────────────────────────────┘ │
│ [from][to]   │ ┌───────────────────────────────────────────────────┐ │
│              │ │ [thumb] Another video                        [n][e]│ │
│ Result type  │ │ …                                               │ │
│ ☑ Metadata   │ └───────────────────────────────────────────────────┘ │
│ ☑ Segments   │ ┌───────────────────────────────────────────────────┐ │
│ ☑ Transcript │ │ [thumb] Third video                          [n][e]│ │
│ ☑ Links      │ └───────────────────────────────────────────────────┘ │
│ ☑ Repos      │                        …                              │
│ ☑ Notes      │ ◀ 1 2 3 4 ▶                          25 per page ▾   │ │
│              │                                                       │
│ ☑ Has trans. │ RECENT SEARCHES                                       │
│ ☑ Has repo   │ • github idea search   (3 high-signal)                │
│ ☐ Has notes  │ • postgres vector      • llm classification           │
│              │ [ Clear history ]                                     │
│ Status       │                                                       │
│ ☑ Processed  │                                                       │
│ ☑ Warnings   │                                                       │
│ ☐ Failed     │                                                       │
│              │                                                       │
│ [Apply] [✕]  │                                                       │
└──────────────┴───────────────────────────────────────────────────────┘
```

**Mobile (<768px):**

```
┌─────────────────────────────┐
│ ┌─────────────────────────┐ │
│ │ 🔍 search…        [⚙][✕]│ │  ← ⚙ opens filter bottom sheet
│ └─────────────────────────┘ │
│ 100 results · 312ms         │
│ ┌─────────────────────────┐ │
│ │ [thumb]                 │ │
│ │ Video title             │ │
│ │ Channel · Jul 16 · 24m  │ │
│ │ "…primary snippet…"     │ │
│ │ ▶12:34 🔗 🌐 📝    87%  │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ card                    │ │
│ └─────────────────────────┘ │
│          …                  │
│ [ Load more ]               │
│ ─────────────────────────── │
│ Recent: • q1  • q2  • q3    │
└─────────────────────────────┘

Filter bottom sheet (on ⚙):
┌─────────────────────────────┐
│ ═══ (drag handle)           │
│ Filters              [✕]    │
│ Channel          ☑ TonbisAI │
│ Date range    [from] [to]   │
│ Result type      ☑ Metadata │
│                  ☑ Segments │
│ Has…     ☑transcript ☑repo  │
│ Status    ☑processed ☑warn  │
│ ┌─────────────────────────┐ │
│ │      Apply filters      │ │
│ └─────────────────────────┘ │
│ [ Reset all ]               │
└─────────────────────────────┘
```

**Key interactions:**

- Search input autofocused on page entry (desktop); `/` keyboard shortcut focuses it from anywhere (§6.2).
- Typing + Enter (or debounced submit) updates URL and executes `POST /api/search`.
- Text/vector weight slider adjusts `ranking.textWeight`/`vectorWeight` for subsequent searches (setting persisted via `PUT /api/settings` keys `search.textWeight`/`search.vectorWeight`).
- Filters apply via Apply button (mobile sheet) or immediately (desktop sidebar), reflected in URL.
- Result cards: click/Enter expands inline (§5.2); action links open artifacts in new tabs and fire `POST /api/user-interaction-events` with `eventType` (`timestamp_opened`, `repository_opened`, `website_opened`, `note_opened`) and rank metadata.
- Recent searches panel: click re-runs; `DELETE /api/recent-searches` clears all (with confirm).
- Pagination: page-based (`page`, `pageSize` default 25) on desktop; "Load more" appending on mobile.
- Keyboard navigation: `↑↓` moves result focus, `Enter` expands, letter shortcuts open artifacts (§6.2).

**Data dependencies:**

- `POST /api/search` — the core call; request/response per API_SPEC §8 (video-cluster shape used verbatim by the result card, §5.1/5.2).
- `GET /api/search/suggestions?q=…` — optional typeahead.
- `GET /api/recent-searches`, `DELETE /api/recent-searches` — recent panel.
- `PUT /api/settings` — persist ranking weights.
- `POST /api/user-interaction-events` — click signals.
- `GET /api/screenshots/{screenshotId}` — thumbnails and gallery images.

**Empty state (no query yet):**

```
┌──────────────────────────────────────────┐
│         🔍 Search your knowledge base    │
│                                          │
│  Search across video metadata, segments, │
│  transcripts, links, repos, and notes.   │
│                                          │
│  Recent searches:                        │
│  • github idea search  • postgres vector │
└──────────────────────────────────────────┘
```

**Empty state (no results):** "No videos matched **{query}**. Try fewer filters or a broader phrase. [ Clear filters ]"

**No-channel state:** If search runs before any ingestion: "Nothing to search yet. [ ➕ Add a channel ] [ ▶ Run ingestion ]"

**Error state:** Search failure banner above results area: "Search failed — {error}. [ Retry ]" Previous results remain visible behind the banner if any. Embedding-provider failures surface `queryDiagnostics` hint: "Embedding model unavailable — check Settings → Models."

---

### 4.5 Channels

**Purpose:** Manage the channel list (PRD §2.6): add, pause, remove, backfill, view ingestion status.

**Route:** `/channels`

**Layout:** Responsive — **table/data grid on desktop, cards on mobile**.

**Desktop (≥1024px):**

```
┌──────────────────────────────────────────────────────────────────────┐
│ Channels                                    [ ➕ Add channel ]       │
├──────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ ➕ Add a channel                                                 │ │
│ │ YouTube URL / handle / channel ID                                │ │
│ │ ┌──────────────────────────────────────────┐  [ Add ]            │ │
│ │ │ https://www.youtube.com/@TonbisAIGarage  │                     │ │
│ │ └──────────────────────────────────────────┘                     │ │
│ │ ✔ Resolved: Tonbis AI Garage (UC…)                               │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│ Name              │ Last ingestion    │ Status        │ Actions      │
│ ──────────────────┼───────────────────┼───────────────┼─────────────│
│ ▶ Tonbis AI       │ Jul 17, 6:00 AM   │ ✔ processed   │ [Run][Fill][⏸][⋯] │
│ ⏸ Paused Chan     │ Jul 10, 6:00 AM   │ ⚠ warnings    │ [Run][Fill][▶][⋯] │
│                                                                      │
│ Row ⋯ menu: Edit defaults · Purge screenshots · Delete               │
└──────────────────────────────────────────────────────────────────────┘
```

**Mobile (<768px):**

```
┌─────────────────────────────┐
│ Channels      [ ➕ Add ]    │
│ ┌─────────────────────────┐ │
│ │ ▶ Tonbis AI Garage      │ │
│ │ Last run: Jul 17 6:00AM │ │
│ │ ✔ processed             │ │
│ │ [▶ Run] [⏮ Backfill][⋯] │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ ⏸ Paused Channel        │ │
│ │ ⚠ warnings              │ │
│ └─────────────────────────┘ │
└─────────────────────────────┘
```

**Key interactions:**

- Add channel: inline form validating the YouTube source → `POST /api/channels` returns resolved canonical metadata shown as confirmation.
- Run now (per channel): `POST /api/channels/{channelId}/ingestion/run` → toast + SSE run tracking.
- Backfill: dialog with days + max-videos → `POST /api/channels/{channelId}/ingestion/backfill`.
- Pause/resume: `PUT /api/channels/{channelId}` (`isPaused`).
- Delete: confirmation dialog; destructive delete with `deleteRelatedData=true` requires typing the channel name to confirm (PRD §2.6 destructive-confirm rule).
- Row/card click → channel detail drawer or expanded row showing defaults (max-age, backfill max), recent ingestion status, edit form (`PUT /api/channels/{channelId}`).

**Data dependencies:**

- `GET /api/channels?includePaused=true` — list.
- `POST /api/channels`, `PUT /api/channels/{channelId}`, `DELETE /api/channels/{channelId}` — CRUD.
- `POST /api/channels/{channelId}/ingestion/run`, `POST /api/channels/{channelId}/ingestion/backfill` — triggers.
- `DELETE /api/channels/{channelId}/screenshots` — purge action.
- SSE `ingestion` stream — live status updates while runs execute.

**Empty state:** "No channels yet. Channels tell Streaming Digest which YouTube creators to follow. [ ➕ Add your first channel ]"

**Error state:** Add-channel validation failure shows inline: "That doesn't look like a supported YouTube channel URL, handle, or channel ID." List load failure → error card with retry.

---

### 4.6 Ingestion Runs

**Purpose:** History and live status of ingestion runs (PRD §2.6).

**Route:** `/ingestion`

**Layout:** Table on desktop, cards on mobile. Live runs pin to the top with progress.

```
DESKTOP
┌──────────────────────────────────────────────────────────────────────┐
│ Ingestion Runs                          [ ▶ Run ingestion now ]      │
├──────────────────────────────────────────────────────────────────────┤
│ ● LIVE — Scheduled run #42                    ████████░░ 67%          │
│   3 channels · 4 of 5 videos processed        [ View → ]             │
├──────────────────────────────────────────────────────────────────────┤
│ Started        │ Type      │ Status              │ Summary │ Actions  │
│ ───────────────┼───────────┼─────────────────────┼─────────┼─────────│
│ Jul 17 6:00 AM │ scheduled │ ✔ processed          │ 5 new, 2 repos │ [→] │
│ Jul 16 6:00 AM │ scheduled │ ⚠ with warnings      │ 4 new, 1 fail  │ [→] │
│ Jul 15 2:13 PM │ manual    │ ✖ failed             │ 0 of 3         │ [→] │
│ Jul 15 6:00 AM │ backfill  │ ⏸ deferred           │ rate-limited   │ [→] │
│                                                                      │
│ Filters: [Status ▾] [Type ▾] [From] [To]        ◀ 1 2 3 ▶            │
└──────────────────────────────────────────────────────────────────────┘

MOBILE
┌─────────────────────────────┐
│ Ingestion      [ ▶ Run now ]│
│ ┌─────────────────────────┐ │
│ │ ● LIVE scheduled #42    │ │
│ │ ████████░░ 67%          │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ Jul 17 6:00 · scheduled │ │
│ │ ✔ 5 new · 2 repos   [→] │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ Jul 16 6:00 · scheduled │ │
│ │ ⚠ 4 new · 1 failed  [→] │ │
│ └─────────────────────────┘ │
└─────────────────────────────┘
```

**Key interactions:**

- Run ingestion now → `POST /api/ingestion/run` → live card appears via SSE `ingestion` stream.
- Row/card → `/ingestion/runs/{runId}` (§4.7).
- Filters (`status`, `runType`, `from`, `to`) and paging map to `GET /api/ingestion/runs` query params; state in URL.

**Data dependencies:**

- `GET /api/ingestion/runs` — paged list.
- `POST /api/ingestion/run` — manual trigger.
- SSE `ingestion` stream — live progress for active runs (§6.1).

**Empty state:** "No ingestion runs yet. [ ▶ Run your first ingestion ] (Requires at least one channel — [ Add a channel ])"

**Error state:** List failure → error card with retry. A failed manual trigger → toast with the API error summary.

---

### 4.7 Ingestion Run Detail

**Purpose:** Deep operational view of one run (PRD §2.6; IMPLEMENTATION_PLAN Task 2.3a): stage timeline, per-video items with retry, deferments, operational links.

**Route:** `/ingestion/runs/{runId}`

**Layout:**

```
DESKTOP
┌──────────────────────────────────────────────────────────────────────┐
│ ← Ingestion Runs   Scheduled run #42 · Jul 17, 2026 6:00 AM          │
│ ✔ processed with warnings · completed 6:15 AM (15m 4s)               │
│ [ Hangfire ] [ Grafana ] [ Logs ] [ Traces ]    [ Retry failed ]     │
├──────────────────────────────────────────────────────────────────────┤
│ ⏸ RATE-LIMIT DEFERRED — GitHub API                                   │
│   Website/repo processing paused. Resumes 7:15 AM.  [ Clear ]        │
├──────────────────────────────────────────────────────────────────────┤
│ SUMMARY                                                              │
│ Channels 3 · Found 5 · Ingested 4 · Failed 1 · Repos 2 · Sites 3     │
├──────────────────────────────────────────────────────────────────────┤
│ STAGE TIMELINE                                                       │
│ metadata     ██████████████████████████████ 5/5 ✔                    │
│ transcript   ██████████████████████████████ 5/5 ✔                    │
│ whisper      ████████████████████░░░░░░░░░░ 3/5 ⚠ 2 skipped          │
│ segments     ██████████████████████████████ 5/5 ✔                    │
│ screenshots  ██████████████████████████████ 5/5 ✔                    │
│ links        ██████████████████████████████ 5/5 ✔                    │
│ repo/site    ██████████████░░░░░░░░░░░░░░░░ 2/5 ⏸ deferred           │
│ embeddings   ███████████████████████████░░░ 4/5 ⚠ 1 failed           │
├──────────────────────────────────────────────────────────────────────┤
│ VIDEOS                                                               │
│ ▼ ✔ Video title one — Channel                          [ Retry ▾ ]   │
│     stages: all ✔ · 12 segments · 12 screenshots · 3 links · 1 repo  │
│     transcript ✔ · embeddings ✔                                      │
│ ▶ ✖ Video title two — Channel                          [ Retry ▾ ]   │
│ ▶ ⚠ Video title three — Channel (no captions, whisper deferred)      │
│ ▶ ⏸ Video title four — Channel (repo stage deferred)                 │
│                                                                      │
│ Expanded item:                                                       │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ ✖ Video title two                                    [ Retry ▾ ] │ │
│ │ Stage: embeddings — "Ollama timeout after 30s"                   │ │
│ │ [ Retry failed stages ] [ Retry all stages ]                     │ │
│ │ transcript ✔ · segments ✔ · screenshots ✔ · links ✔ · embed ✖    │ │
│ │ Extracted: 4 links · 1 repo (github.com/owner/repo) · 2 sites    │ │
│ │ [ View logs ] [ View trace ]                                     │ │
│ └──────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘

MOBILE
┌─────────────────────────────┐
│ ← Runs   Run #42            │
│ ✔ warnings · 15m 4s         │
│ [Hangfire][Grafana][Logs]   │
│ ┌─────────────────────────┐ │
│ │ ⏸ DEFERRED — GitHub API │ │
│ │ Resumes 7:15 AM         │ │
│ └─────────────────────────┘ │
│ 3 ch · 5 found · 4 ok · 1 ✖ │
│ STAGES                      │
│ metadata   ████████ 5/5 ✔   │
│ transcript ████████ 5/5 ✔   │
│ repo/site  ███░░░░░ 2/5 ⏸   │
│ embeddings ██████░░ 4/5 ⚠   │
│ VIDEOS                      │
│ ▶ ✔ Video one    [Retry ▾]  │
│ ▶ ✖ Video two    [Retry ▾]  │
│ ▶ ⚠ Video three  [Retry ▾]  │
└─────────────────────────────┘
```

**Key interactions:**

- Stage timeline renders per-stage progress bars with completion counts from `stageSummary` (live-updated via SSE `ingestion` stream while the run is active).
- Per-video rows expand inline to show stage statuses, extracted artifacts (links/repos/websites counts), transcript/screenshot/embedding status, failure reason, and log/trace links.
- Retry buttons: per-item retry → `POST /api/ingestion/items/{itemId}/retry` or `POST /api/videos/{videoId}/retry` with `stages` + `retryFailedOnly`; run-level "Retry failed" → `POST /api/batch/retry`.
- Deferment banner: prominent amber panel when the run has active deferments (`deferments` in run payload); Clear → `POST /api/rate-limit-deferments/{id}/clear` with confirmation.
- Operational links in header: Hangfire (`links.hangfire`), Grafana, logs, traces from `GET /api/admin/observability-links` + run `links.trace`. Rendered only when observability is enabled (ARCHITECTURE §6.3).

**Data dependencies:**

- `GET /api/ingestion/runs/{runId}` — run detail: summary, `stageSummary`, warnings, deferments, links.
- `GET /api/ingestion/runs/{runId}/items` — per-video items (filterable by `status`, `stage`, `itemType`).
- `POST /api/ingestion/items/{itemId}/retry`, `POST /api/videos/{videoId}/retry`, `POST /api/batch/retry` — retries.
- `GET /api/rate-limit-deferments`, `POST /api/rate-limit-deferments/{id}/clear` — deferments.
- `GET /api/admin/observability-links` — operational links.
- SSE `ingestion` stream — live stage/item updates for active runs.

**Empty state:** A run with zero videos found: "No new videos found in this run. All channels are up to date. ✔"

**Error state:** Run detail load failure → full-page error with retry and back link. Individual item retry failure → inline error on the item row.

---

### 4.8 Admin/Settings

**Purpose:** All configuration and operational surface (PRD §2.6, §2.10; API_SPEC §5, §17, §18).

**Route:** `/settings` (tabbed; `/settings/{tab}` deep-links)

**Layout:** Tabbed page — domain tabs: **General, Models, Matrix, Observability, Backup**.

```
DESKTOP
┌──────────────────────────────────────────────────────────────────────┐
│ Settings                                                             │
│ ┌──────────┬────────┬────────┬───────────────┬─────────┐             │
│ │ General  │ Models │ Matrix │ Observability │ Backup  │             │
│ └──────────┴────────┴────────┴───────────────┴─────────┘             │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ GENERAL                                                          │ │
│ │                                                                  │ │
│ │  Appearance                                                      │ │
│ │  Theme:  (•) System   ( ) Light   ( ) Dark                       │ │
│ │                                                                  │ │
│ │  Ingestion                                                       │ │
│ │  Default max-age lookback (days):      [ 30  ]                   │ │
│ │  Default ingestion schedule:           [ 06:00 ] 🕕 local        │ │
│ │  Max segments per video:               [ 60  ]                   │ │
│ │  Screenshot offset (seconds):          [ 5   ]                   │ │
│ │                                                                  │ │
│ │  Search                                                          │ │
│ │  Text weight [────●──────] 0.5   Vector weight 0.5               │ │
│ │                                                                  │ │
│ │  Danger zone                                                     │ │
│ │  [ Clear recent-search history ]                                 │ │
│ │                                                                  │ │
│ │                                        [ Save changes ]          │ │
│ └──────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

**Tab contents:**

| Tab | Contents | Endpoints |
|---|---|---|
| **General** | Theme (Light/Dark/System), ingestion defaults (max-age, schedule, segment cap, screenshot offset), search weights, clear recent searches, notification toggles | `GET/PUT /api/settings`, `DELETE /api/recent-searches` |
| **Models** | Active embedding/LLM/audio models with verify + test buttons; model options with download; activate-model flows with explicit regeneration confirmation (stale count shown before confirm); readiness checklist re-verify links | `GET /api/models/options`, `POST /api/models/download`, `POST /api/models/verify`, `POST /api/models/activate-*`, `POST /api/admin/test-embedding`, `POST /api/admin/test-audio-to-text` |
| **Matrix** | Room ID, bot status, notification toggles (manual/scheduled runs), test send | `PUT /api/settings`, `POST /api/admin/test-matrix` |
| **Observability** | Links to Grafana/Prometheus/Loki/Tempo/Hangfire (rendered only when enabled, ARCHITECTURE §6.3); dependency health summary; retention policy display | `GET /api/admin/observability-links`, `GET /api/admin/health` |
| **Backup** | Backup now button, backup list with verify, maintenance status (versions, compatibility, stale derived data), upgrade preview + apply-migrations | `POST /api/admin/backups`, `GET /api/admin/backups`, `GET /api/admin/maintenance/status`, `GET /api/admin/upgrade/preview`, `POST /api/admin/upgrade/apply-migrations` |

**Key interactions:**

- Theme selector applies immediately (§6.4) and persists to settings.
- Settings form: dirty tracking; Save enabled only when changed; `PUT /api/settings` with the flat key shape from API_SPEC §5.
- Model activation: two-step — shows `staleEmbeddingCount` consequence, requires explicit `confirmedRegeneration: true` checkbox before submit.
- Test buttons show inline spinner → success (with latency/model details) or failure (with error summary).

**Empty state:** Backup tab with no backups: "No backups yet. [ Create your first backup ] — recommended before any upgrade."

**Error state:** Save failure → inline banner with the validation errors from the API error shape. Health check failures render as amber/red status rows in Observability, never page-level crashes.

---

## 5. Component Specifications

### 5.1 Search Result Card (Collapsed)

**Purpose:** Two equal jobs (ARCHITECTURE §4.10): help the user decide whether to expand, and provide immediate jumps to available artifacts. Thumbnail + content first — the YouTube thumbnail is on the collapsed card.

**Data mapping** — every field from PRD §2.5 collapsed state and API_SPEC §8 response item, mapped to layout:

```
┌──────────────────────────────────────────────────────────────────────┐
│ ┌──────────┐  Video title (effective: override ?? original)   [n][e] │ ← title = `title`
│ │          │  Channel Name · Jul 16, 2026 · 24:12 · 12 matches inside│ ← channel.name,
│ │ YT thumb │  ─────────────────────────────────────────────────────  │   publishedAt, duration,
│ │  16:9    │  "…snippet of the primary match with <mark>highlight…"  │   matchesInsideCount
│ │          │  segment · transcript_chunk · 87% relative similarity   │ ← primaryMatch.snippet
│ └──────────┘  ─────────────────────────────────────────────────────  │   (matched text marked)
│ ▶ 12:34  🔗 repo  🌐 site  📝 note  ⚠ 2 warnings      score 0.87     │ ← primaryMatch: documentType,
└──────────────────────────────────────────────────────────────────────┘   matchedField,
```

| Visual element | Source field (API_SPEC §8 item) |
|---|---|
| Thumbnail | `primaryMatch.screenshotUrl` → `GET /api/screenshots/{id}`; falls back to YouTube thumbnail from video metadata |
| Title | `title` (effective: override else original) |
| Channel | `channel.name` (links to `channel.url`) |
| Publish date | `publishedAt` |
| Duration | from linked video metadata |
| Match count | `matchesInsideCount` — "12 matches inside" |
| Primary match snippet | `primaryMatch.snippet` with `<mark>` highlights |
| Match type | `primaryMatch.documentType` + `matchedField` ("segment · transcript_chunk") |
| Score | `score` + `relativeSimilarityPercent` ("87%") with tooltip: `queryDiagnostics.relativeSimilarityExplanation` |
| Watch timestamp link | `primaryMatch.watchUrl` (`links.watch`) — opens YouTube at `startSeconds` |
| Repo link | `links.repositories[0]` |
| Website link | `links.websites[0]` |
| Note indicator/button | `hasNotes` — `📝` filled when notes exist |
| Edit action | `[e]` icon button → Edit modal (§5.3) |
| Warning indicator | `warnings[]` + `processingStatus` (`processed_with_warnings` → ⚠ badge with count) |
| Retry button | shown when `processingStatus` is failed/retryable → `POST /api/videos/{videoId}/retry` |

**Behavior:**

- Whole card is keyboard-focusable; `Enter` expands (§5.2).
- Action links open in new tabs; each fires `POST /api/user-interaction-events` with the matching `eventType` and rank metadata.
- Warning badge tooltip lists `warnings[].message`.
- Border/accent color varies by state: default (processed), amber (warnings), red (failed).

### 5.2 Search Result Card (Expanded)

**Purpose:** Full detail without leaving the results list (PRD §2.5: no separate detail page for MVP). **Inline accordion expansion — pushes content down**, not an overlay.

**Contents** (every PRD §2.5 expanded field):

```
┌──────────────────────────────────────────────────────────────────────┐
│ ┌──────────┐  Video title                                      [n][e]│
│ │  thumb   │  Channel · Jul 16, 2026 · 24:12 · 12 matches inside     │
│ └──────────┘                                                          │
│ ─────────────────────────────────────────────────────────────────────│
│ ▼ EXPANDED                                                           │
│                                                                      │
│ ALL MATCHES (12)                                    score 0.87 · 87% │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ 12:34  segment · transcript_chunk · 0.91                         │ │
│ │ "…snippet with highlights…"                    [ ▶ watch ]        │ │
│ │ 08:12  segment · segment_title · 0.84                            │ │
│ │ "…second match…"                               [ ▶ watch ]        │ │
│ │ README  repository · readme_chunk · 0.79                         │ │
│ │ "…third match…"                                [ 🔗 repo ]        │ │
│ │                              [ Show all 12 matches ]              │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│ SCREENSHOTS                                                          │
│ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐                              │
│ │ seg1│ │ seg2│ │ seg3│ │ seg4│ │ seg5│   ← horizontal scroll strip  │
│ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘     click → ▶ at that time  │
│                                                                      │
│ TIMESTAMP LINKS                                                      │
│ [ 0:00 Intro ] [ 2:14 Setup ] [ 8:12 Core idea ] [ 12:34 Demo ]      │
│                                                                      │
│ LINKS                                                                │
│ 🔗 github.com/owner/repo        🌐 example.com/resource              │
│ 🔗 deepwiki.com/owner/repo                                           │
│                                                                      │
│ RELATED ITEMS (across the corpus)                                    │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ ▎video_cluster · Another video title ················· 81%       │ │
│ │ ▎repository · owner/other-repo ······················· 76%       │ │
│ │ ▎website · example.com/similar ······················· 72%       │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│ (related rows: border-left color varies by `type`; % = relativeSim.) │
│                                                                      │
│ SCORE BREAKDOWN                                                      │
│ max doc 0.91 · avg top-3 0.82 · coverage 0.75 · note +0.08 ·         │
│ interaction +0.02 · text 0.5 / vector 0.5                            │
│ matched types: transcript_chunk, repository_readme_chunk             │
│                                                                      │
│ ⚠ PROCESSING WARNINGS                                                │
│ • website_scrape_failed — One linked website could not be scraped.   │
│                                                                      │
│ [ ✏ Edit ]  [ 📝 Notes ]                               [ Collapse ▲ ]│
└──────────────────────────────────────────────────────────────────────┘
```

**Data mapping:**

| Section | Source |
|---|---|
| All submatches | `matches[]` (expanded load fetches full match list; collapsed payload has `matches: []`) |
| Segment screenshot gallery | `GET /api/videos/{videoId}/segments` → per-segment screenshot URLs |
| Timestamp links | segment list with `startSeconds` → YouTube `?t=` URLs |
| Repo/website links | `links.repositories[]`, `links.websites[]` |
| Related items | `relatedItems[]` — type-tagged rows with `relativeSimilarityPercent` |
| Score breakdown | `scoreComponents` (all fields enumerated) |
| Processing warnings | `warnings[]`, `processingStatus` |
| Notes | `GET /api/notes?targetType=video&targetId={videoId}` |

**Behavior:**

- Expansion loads submatches + segments lazily on first expand; cached for the session.
- Related items render inside the same container, visually distinguished by border color per `type` (PRD §2.5).
- Only one card expanded at a time per viewport scroll position is allowed (expanding another collapses the previous on mobile; desktop allows multiple).
- `[e]` / `✏ Edit` → Edit modal (§5.3). `📝 Notes` → Note modal (§5.4).

### 5.3 Edit Modal

**Purpose:** Override any scraped field (PRD §2.5 edit scope; API_SPEC §11). Well-organized tabs, not one giant form.

**Layout:** `FluentDialog` — large (max-width ~900px desktop, full-screen mobile). **5 tabs: Video, Segments, Links, Repository, Note.**

```
┌──────────────────────────────────────────────────────────────────────┐
│ Edit — Video title                                            [ ✕ ]  │
│ ┌────────┬──────────┬───────┬────────────┬──────┐                    │
│ │ Video  │ Segments │ Links │ Repository │ Note │                    │
│ └────────┴──────────┴───────┴────────────┴──────┘                    │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ VIDEO TAB                                                        │ │
│ │                                                                  │ │
│ │ Title                                                            │ │
│ │ ┌────────────┬────────────────────────────────────────────────┐  │ │
│ │ │ Original   │ "Original scraped title"                       │  │ │
│ │ ├────────────┼────────────────────────────────────────────────┤  │ │
│ │ │ Override   │ [ "User-edited title"                        ] │  │ │
│ │ ├────────────┼────────────────────────────────────────────────┤  │ │
│ │ │ Effective  │ "User-edited title"  (used in search/display)  │  │ │
│ │ └────────────┴────────────────────────────────────────────────┘  │ │
│ │                                                                  │ │
│ │ Description                                                      │ │
│ │ ┌────────────┬────────────────────────────────────────────────┐  │ │
│ │ │ Original   │ "Scraped description…"                         │  │ │
│ │ │ Override   │ [ multi-line                                 ] │  │ │
│ │ │ Effective  │ "…"                                            │  │ │
│ │ └────────────┴────────────────────────────────────────────────┘  │ │
│ │                                                                  │ │
│ │ ℹ Saving marks affected search docs stale and queues embedding   │ │
│ │   regeneration using effective values.                           │ │
│ │                                                                  │ │
│ │                          [ Cancel ]   [ Save changes ]           │ │
│ └──────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

**Tab specifications:**

| Tab | Editable fields (original/override/effective side by side) | Save endpoint |
|---|---|---|
| **Video** | title, description, author | `PUT /api/videos/{videoId}/overrides` |
| **Segments** | per-segment title, summary/description (segment picker list at top; transcript cue text edits under selected segment) | `PUT /api/segments/{segmentId}/overrides`, `PUT /api/transcript-cues/{cueId}/overrides` |
| **Links** | per-link title, description, classification (dropdown of the PRD §2.2 classes) | `PUT /api/external-resources/{resourceId}/overrides` |
| **Repository** | description, primary language, topics | `PUT /api/repositories/{repositoryId}/overrides` |
| **Note** | Note markdown editor (EasyMDE) — same editing surface as §5.4 | `POST/PUT /api/notes` |

**Behavior:**

- Every scraped field renders the **original / override / effective** triple per API_SPEC §2.6. Original is read-only; override is the input; effective previews what search/display uses.
- Classification change shows the confirmation note: **"Future similar links will use this correction"** (PRD §2.5) — also shown when re-viewing a previously corrected item.
- Save → immediate-mutation response; UI shows queued regeneration (`queuedOperations`) as a toast: "Saved. Embeddings regenerating in background."
- Override history: "View history" link per field → `GET /api/overrides/history?entityType=…&fieldName=…` shown in a small popover.
- Validation errors from the API error shape render inline per field.

### 5.4 Note Modal

**Purpose:** Create/edit a markdown note attached to a video, segment, external link, or repository (PRD §2.5). **Modal-only — there is no dedicated notes page.**

**Layout:** `FluentDialog` with **EasyMDE** markdown editor.

```
┌──────────────────────────────────────────────────────────┐
│ 📝 Note — Video title                              [ ✕ ] │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Title                                                │ │
│ │ [ Idea for my search project                       ] │ │
│ │                                                      │ │
│ │ ┌──────────────────────────────────────────────────┐ │ │
│ │ │ EasyMDE toolbar: B I ⎘ ☰ 🔗 👁                    │ │ │
│ │ │ ┌──────────────────────────────────────────────┐ │ │ │
│ │ │ │ Use the approach at 12:34 for the GitHub     │ │ │ │
│ │ │ │ crawl. Combine with pgvector ranking.        │ │ │ │
│ │ │ └──────────────────────────────────────────────┘ │ │ │
│ │ └──────────────────────────────────────────────────┘ │ │
│ │                                                      │ │
│ │ ℹ Notes are embedded and boost this item in search.  │ │
│ │                                                      │ │
│ │        [ Delete note ]      [ Cancel ]  [ Save ]     │ │
│ └──────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

**Behavior:**

- Target: `{ targetType: "video" | "segment" | "external_resource" | "repository", targetId }`.
- Create: `POST /api/notes`; update: `PUT /api/notes/{noteId}`; delete: `DELETE /api/notes/{noteId}` (soft delete, with confirm).
- Save response includes `embeddingStatus` — UI shows "Note saved — embedding queued" toast; the note indicator on the card fills immediately.
- EasyMDE loads from CDN or bundled static asset; falls back to a plain textarea with markdown preview tab if the asset fails.
- If a note is cleared/emptied and saved, the embedding and parent cluster aggregate update (PRD §2.5) — UI notes this in the toast.

### 5.5 Toast Notifications

**Purpose:** Completion/failure feedback for background operations, delivered via SSE (§6.1) and local actions.

**Implementation:** `FluentToast` / Fluent UI message-bar stack, top-right (desktop) / top-full-width (mobile), auto-dismiss 5s for success, sticky for failures.

| Trigger | Style | Example |
|---|---|---|
| Ingestion run completed | Success | "Ingestion complete — 4 new videos, 2 repos. [ View run ]" |
| Ingestion run failed | Error (sticky) | "Ingestion failed — 1 video could not be processed. [ View run ]" |
| Digest available | Info | "Your daily digest is ready. [ Open ]" |
| Operation accepted | Info | "Embedding regeneration queued." |
| Save success | Success | "Saved. Embeddings regenerating in background." |
| Action failure | Error (sticky) | "Couldn't save — {error}. [ Retry ]" |

Toasts with links deep-link to the relevant page (`/ingestion/runs/{id}`, `/`, `/settings/models`).

### 5.6 Navigation Badges

**Purpose:** Live counts on nav items, updated via SSE — not polling.

**Implementation:** Small count pill rendered beside the nav label (`FluentBadge`-style).

| Nav item | Badge source |
|---|---|
| Dashboard | Pending inbox item count (SSE `inbox` stream) |
| Ingestion | Active/running run count (SSE `ingestion` stream) |

Badge hides at zero. Amber styling for warnings, red for failures, neutral otherwise. Screen-reader text: "{n} pending items".

---

## 6. Interaction Patterns

### 6.1 Server-Sent Events (SSE)

Real-time updates use **SSE, not polling**. Four streams. Polling is a fallback only.

**Connection lifecycle:**

1. Client opens `EventSource` connections after successful auth bootstrap (§3.4). All SSE endpoints are same-origin, cookie-authenticated.
2. On `error` / connection drop: exponential backoff reconnect (1s → 2s → 4s → 8s → max 30s, jittered).
3. After **3 consecutive failed reconnects**, the client switches to **fallback polling** every 30s for the affected stream's underlying REST endpoint, and shows a subtle "Live updates paused — reconnecting…" pill in the top bar.
4. When an SSE `open` succeeds again, polling stops and the pill clears.
5. All four streams may be multiplexed over one endpoint (`/api/events`) with typed events, or exposed as four endpoints — the client treats them as four logical streams regardless of transport factoring.

**Streams and event payloads:**

**① Ingestion run progress** — drives §4.6 live card, §4.7 stage timeline, ingestion nav badge.

```json
{
  "type": "ingestion.progress",
  "ingestionRunId": "uuid",
  "operationId": "uuid",
  "status": "running",
  "percentComplete": 67,
  "stageSummary": [{ "stage": "transcript", "status": "completed", "count": 4 }],
  "currentItem": { "videoId": "uuid", "title": "...", "stage": "embeddings" }
}
```

Terminal events: `ingestion.completed`, `ingestion.failed` — trigger toast (§5.5) and final refresh of the run.

**② Pending-action inbox changes** — drives dashboard inbox widget + dashboard nav badge.

```json
{
  "type": "inbox.changed",
  "pendingCount": 3,
  "items": [
    { "kind": "failed_ingestion", "severity": "error", "count": 1, "link": "/ingestion/runs/uuid" },
    { "kind": "rate_limit_deferment", "severity": "warning", "count": 1, "link": "/ingestion/runs/uuid" }
  ]
}
```

**③ Operation status** — drives progress for accepted operations (model downloads, embedding reprocessing, backups, batch retries).

```json
{
  "type": "operation.status",
  "operationId": "uuid",
  "operationType": "reprocess_embeddings",
  "status": "running",
  "percentComplete": 41,
  "statusUrl": "/api/operations/uuid"
}
```

Terminal `operation.completed` / `operation.failed` → toast + invalidate affected queries (e.g., stale-embedding count).

**④ Digest availability** — drives dashboard digest widget + toast.

```json
{
  "type": "digest.available",
  "ingestionRunId": "uuid",
  "date": "2026-07-18",
  "newVideoCount": 4,
  "highSignalMatchCount": 2
}
```

**How SSE updates UI state:** Each stream maps to a client-side state store slice. Events merge into the store; components bound to the store re-render automatically (Blazor `StateHasChanged` after async event dispatch on the synchronization context). SSE events never replace a full fetch on page load — they only patch already-loaded views. On reconnect after a gap, the client re-fetches the affected resource once (`Last-Event-ID` if supported, else simple refetch) to close any event gap.

### 6.2 Keyboard Shortcuts

Full shortcut set for the search journey. Shortcuts are global unless noted; single-letter shortcuts are suppressed while focus is in an input, textarea, or contenteditable element.

| Key | Context | Action |
|---|---|---|
| `/` | Global | Focus search input |
| `Esc` | Global | Clear search input (when focused) / close modal, drawer, or expanded card |
| `↑` / `↓` | Results list | Move focus between result cards |
| `Enter` | Focused result | Expand/collapse the card |
| `t` | Focused result | Open primary timestamp link (`t` = time) |
| `r` | Focused result | Open repository link |
| `w` | Focused result | Open website link |
| `n` | Focused result | Open note modal |
| `e` | Focused result | Open edit modal |
| `?` | Global | Toggle shortcuts help overlay |

**Shortcuts help overlay** (on `?`):

```
┌──────────────────────────────────────┐
│ Keyboard shortcuts             [ ✕ ] │
│ ──────────────────────────────────── │
│  /      Focus search                 │
│  Esc    Clear / close                │
│  ↑ ↓    Navigate results             │
│  Enter  Expand result                │
│  t      Open timestamp               │
│  r      Open repository              │
│  w      Open website                 │
│  n      Open note                    │
│  e      Edit                         │
│  ?      This help                    │
└──────────────────────────────────────┘
```

Implementation: a single global keydown handler service; cards expose their action URLs to the handler when focused. All shortcuts have visible equivalents — keyboard is parity, never the only path.

### 6.3 URL State Management

Search query and filters are fully reflected in the URL for bookmarking, browser history, and refresh persistence.

**Format:**

```
/search?q=code+project+ideas&channels=uuid1,uuid2&from=2026-01-01&to=2026-07-18
        &types=transcript_chunk,repository_readme_chunk&hasTranscript=true
        &hasRepo=true&hasNotes=false&status=processed,processed_with_warnings
        &textWeight=0.5&vectorWeight=0.5&page=2
```

**Rules:**

- Every state change (query submit, filter apply, page change) pushes a new history entry (`NavigationManager.NavigateTo` with replace=false for query/filter changes, replace=true for in-page refinements like slider drags that settle).
- Page load parses the query string into the search request DTO and executes immediately — a pasted URL reproduces the exact search.
- Unknown/invalid params are ignored, not errors.
- Filter chips above results show active filters with individual ✕ removal (each removal updates URL and re-searches).
- The same pattern applies (lightly) to Ingestion Runs filters (`/ingestion?status=failed&type=scheduled`) and Settings tabs (`/settings/models`).

### 6.4 Theme System

**Modes:** Light, Dark, **System (default)**. Selected in Settings → General; quick-switch in the avatar menu.

**Implementation:**

- Fluent UI Blazor theming: `FluentDesignTheme` component with `Mode` bound to the user's setting.
- System mode follows `prefers-color-scheme` via a `matchMedia` listener; changes apply live without reload.
- The selection persists server-side in app settings (`PUT /api/settings`) so it follows the user across devices, and is mirrored to `localStorage` for pre-auth flash-free application on the login screen.
- `theme-color` meta tag and manifest `theme_color`/`background_color` (§8.1) track the active mode for correct PWA title-bar/splash rendering.
- No custom color palettes at MVP — Fluent 2 default neutral palette with the app accent color.

---

## 7. Empty States & Error States

**Empty states are actionable** — each names the next required step and provides its CTA:

| Surface | Empty headline | CTA |
|---|---|---|
| Dashboard digest | "Your daily digest will appear here" | [ ➕ Add a channel ] [ ▶ Run ingestion ] |
| Dashboard inbox | "✔ All clear — nothing needs your attention" | — (positive terminal state) |
| Search (no query) | "Search your knowledge base" | recent-search chips |
| Search (no results) | "No videos matched **{query}**" | [ Clear filters ] |
| Search (no channels) | "Nothing to search yet" | [ ➕ Add a channel ] |
| Channels | "No channels yet" | [ ➕ Add your first channel ] |
| Ingestion Runs | "No ingestion runs yet" | [ ▶ Run your first ingestion ] |
| Run Detail (0 videos) | "No new videos found — all channels up to date ✔" | — |
| Backups | "No backups yet" | [ Create your first backup ] |
| Recent searches | "No recent searches" | — (panel hides on dashboard) |

**Error states** follow consistent rules:

1. **Widget-level errors stay local.** A failing widget renders an inline error card with [ Retry ]; sibling widgets keep working.
2. **Page-level load errors** render a centered error panel: what failed, why (API error summary), [ Retry ], and a back/safety link.
3. **Mutation errors** surface via sticky toast + inline field errors when the API returns validation details (API_SPEC §2.1 error shape).
4. **Service-degraded states** (embedding model down, Whisper unavailable, Matrix unconfigured) render as amber banners with a link to the fixing surface (Settings → Models / readiness checklist), never as blocking modals.
5. **SSE disconnect** shows the reconnecting pill (§6.1); the app remains usable with fallback polling.

---

## 8. PWA Specifics

Per ARCHITECTURE §5.2, Streaming Digest is a PWA from day one. The **`pwa-development` skill is the authoritative reference** for manifest fields, service worker registration, and caching strategy — consult it before implementing any PWA behavior. **https://whatpwacando.today/** is the per-platform capability reference.

### 8.1 Web App Manifest

`manifest.json` served as a static asset from the API project:

```json
{
  "name": "Streaming Digest",
  "short_name": "Digest",
  "description": "Your personal YouTube knowledge base",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "background_color": "#0f0f0f",
  "theme_color": "#0f6cbd",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/icon-maskable-192.png", "sizes": "192x192", "type": "image/png", "purpose": "maskable" },
    { "src": "/icons/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" }
  ],
  "shortcuts": [
    { "name": "Search", "url": "/search", "icons": [{ "src": "/icons/shortcut-search.png", "sizes": "192x192" }] },
    { "name": "Daily Digest", "url": "/", "icons": [{ "src": "/icons/shortcut-digest.png", "sizes": "192x192" }] },
    { "name": "Run Ingestion", "url": "/ingestion?action=run", "icons": [{ "src": "/icons/shortcut-run.png", "sizes": "192x192" }] }
  ]
}
```

Maskable icons are required for Android adaptive icons. Splash screens generated per platform guidance in the `pwa-development` skill.

### 8.2 Service Worker

- Registered at startup (`service-worker.js` published by the WASM project as a static asset).
- **MVP scope: lifecycle/install support only** — the worker exists and is kept current from day one, but implements **no offline data caching, no offline search, no background sync** (explicitly MVP+ per ARCHITECTURE §5.2).
- Update flow: on new-version detection, prompt the user with a toast — "A new version is available. [ Update ]" — activating the waiting worker on confirmation (see UPGRADE_PATHS §3.1 for the version-update contract and §3.3 for stale-asset handling).
- The caching groundwork (cache names, precache manifest shape) follows the `pwa-development` skill so offline mode is an incremental addition later, not a rework.

### 8.3 App Shortcuts

Three manifest shortcuts (§8.1), matching the confirmed set:

| Shortcut | Target | Behavior |
|---|---|---|
| **Search** | `/search` | Opens app directly to search with input focused. |
| **Daily Digest** | `/` | Opens dashboard, digest widget scrolled into view. |
| **Run Ingestion** | `/ingestion?action=run` | Opens ingestion page and triggers the run-confirmation dialog (never fires the run without one confirmation tap). |

### 8.4 Platform-Specific Behavior

| Platform | Behavior |
|---|---|
| **Desktop (Chrome/Edge)** | Install prompt via `beforeinstallprompt`; installs to app shelf, launches standalone. App shortcuts on icon right-click/long-press. |
| **Android** | Install via browser prompt / Add to Home Screen; maskable adaptive icon; splash screen; app shortcuts on launcher long-press. Primary mobile path: Matrix notification link → opens installed PWA at the run detail/dashboard. |
| **iOS/iPadOS** | Add to Home Screen only (no install prompt API); `apple-touch-icon` + `apple-mobile-web-app-capable` meta; no app shortcuts support (per whatpwacando.today — degrade silently). |
| **Display-mode detection** | CSS `display-mode: standalone` media query + `navigator.standalone` (iOS) adjust top-bar padding for status areas and suppress in-app install prompts when already installed. |
| **Share target** | `share_target` in manifest is MVP+; reserved for future "share YouTube URL → queue ingestion" flow. |

Capability checks at runtime gate each enhancement; every PWA feature degrades gracefully to plain browser behavior on unsupported platforms.

---

*End of presentation layer specification. Implementation tasks: IMPLEMENTATION_PLAN.md Task 2.3 (app shell), 2.3a (run detail), 2.3b (API conformance), 2.3c (PWA baseline).*
