### Task 9.2: Implement repository metadata adapters

Source: `docs/product/PRD.md` §2.2; `docs/architecture/DATA_MODEL.md` §3.15; ADR-0009

Use unauthenticated public REST APIs by default for GitHub. GitLab and Bitbucket are MVP+. PAT support is MVP+; OAuth is MVP++.

Fetch:

- owner/name.
- default branch.
- description.
- stars/forks where available.
- language/topics where available.
- license SPDX where available.

Rate limits:

- On 429/rate limit, pause all repository processing globally for that host. Defer all active jobs and prevent the next daily run from starting host-repository work until after all deferred jobs are completed. Resume at `Retry-After`, or after one hour when absent.
- Surface active deferment in dashboard and Matrix notification.

Verification:

- Fixture tests per host.
- Rate-limit fixture defers remaining work and resumes later.

