## Quality gates

Before declaring MVP complete:

- `dotnet test` passes.
- Scraper service build/test commands pass (if Node/TypeScript per Task 10.1a).
- Integration tests run against PostgreSQL + pgvector.
- Compose stack starts cleanly.
- Local model health checks pass.
- Audio-to-text health check passes.
- Matrix test notification succeeds; encrypted test notification is MVP+.
- Search recall harness meets the top-3 target on the representative vague-query corpus (Task 12.7).
- Search latency meets P50/P95 targets on the ~500 and ~2,000 video representative datasets (Task 12.8).
- API contract conformance tests pass for all MVP endpoints and the Task 2.3b known-pending list is empty.
- Ingestion handles partial failures and retries.
- Backup/restore dry run documented and tested.
- Daily digest dashboard and pending-action inbox satisfy priority/order requirements.
- Notification audit/outbox retry behavior is verified.
- Video-cluster aggregate embeddings are generated, invalidated, and used for high-signal matching.
- Rate-limit deferments are persisted, enforced, surfaced, and clearable.
- Retention/cleanup jobs handle domain events, telemetry policy, screenshots, and raw debug captures.
- Convergence milestones M1–M4 pass at their declared slices.
- Durable verification evidence is committed per the Verification evidence convention (recall, latency, cross-platform, restore dry-run, prototype comparisons).
