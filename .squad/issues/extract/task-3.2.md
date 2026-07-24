### Task 3.2: Implement channel management UI

UI supports:

- Add public YouTube channel URL/handle/channel ID, e.g. `https://www.youtube.com/@TonbisAIGarage`.
- Validate that the input is a supported YouTube channel source.
- Pause/resume.
- Edit settings.
- Delete with optional related data deletion.

Deletion semantics follow `docs/architecture/DATA_MODEL.md` §9: canonical repositories and external resources shared by multiple videos/links lose only their associations/occurrences and are deleted only when no remaining associations exist, unless the user explicitly requests force purge.

Verification:

- Manual UI test with test channel.
- Non-YouTube and logged-in-only subscription import inputs are rejected or labeled MVP+.
- Integration test: deleting a channel whose repository is shared with another channel's video removes associations but preserves the canonical repository/resource.

## Phase 4: Hangfire and ingestion runs

