### Task 2.6: Implement security conformance tests

Source: `docs/product/PRD.md` §2.9; `docs/architecture/ARCHITECTURE.md` §7; `docs/api/API_SPEC.md` §1, §14, §19–21

Requirements:

- Automated security conformance tests covering: Hangfire dashboard at `/admin/jobs` rejects unauthenticated requests (Task 4.1); the screenshot serving endpoint (`/api/screenshots/{id}`) requires authentication; path traversal on screenshot/media `file_path` serving is rejected (e.g. `../` escapes, absolute paths outside the mounted volume); internal service endpoints (`/internal/matrix/*`, `/internal/scrape/*`, `/internal/audio-to-text/*`) are not reachable through the public API surface; `GET /api/config/runtime` and other diagnostics endpoints never return secret values (bootstrap credentials, DB password, API keys, Matrix tokens); CSRF protection rejects token-less mutations beyond the login flow (extending Task 2.2).
- Scenarios that depend on later-phase endpoints land alongside those phases; this task delivers the harness and the auth/CSRF/secret-leak scenarios available from Phase 2 onward.

Verification:

- Each scenario above has an automated test; harness runs in CI from Phase 2 onward.
- A deliberately introduced secret value in runtime config is asserted absent from all diagnostics responses.

## Phase 3: Channel management

