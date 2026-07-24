### Task 14.3: Integrate ingestion notifications

Send by default for:

- manual runs.
- scheduled runs.

Notifications are excerpts of the stored Digest artifact (ADR-0006) — one assembly, two renderings, so Matrix and dashboard never disagree; notification retry re-renders the same stored payload. High-signal evaluation runs once at digest assembly and is skipped for runs completing during an Embedding Transition (ADR-0008).

Notification content includes:

- New videos ingested.
- New repositories found.
- New websites/resources found.
- Items similar to recent searches.
- Failed/skipped items.
- Active rate-limit deferments where relevant.
- Link to web dashboard ingestion run.

Configurable app settings.

Verification:

- Manual run completion sends Matrix summary. Encrypted/E2EE summary is MVP+.
- Scheduled run notification includes high-signal matches similar to recent searches.

