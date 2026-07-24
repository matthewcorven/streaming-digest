### Task 2.3c: Establish PWA baseline

Set up the PWA foundation declared in `docs/architecture/ARCHITECTURE.md` §5.2 so the app is installable and app-like from the first UI milestone, rather than retrofitting PWA later:

- Web app manifest (name, short name, icons, `start_url`, `display: standalone`, theme/background colors).
- Maskable icons and splash assets for desktop and mobile installs.
- Service worker registration for install/lifecycle only — no offline data caching, offline search, or background sync (full offline mode is MVP+).
- Responsive/mobile-first layout verification across desktop and mobile viewport sizes.
- Consult the `pwa-development` skill as the authoritative pattern reference and https://whatpwacando.today/ for per-platform capability behavior.

Verification:

- App is installable and launches in standalone mode on Chrome/Edge desktop and Android.
- Service worker registers without errors; no offline caching behavior is active yet.
- Layout is usable at mobile viewport sizes for the app shell pages from Task 2.3.

