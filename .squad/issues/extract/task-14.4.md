### Task 14.4: Implement notification audit and outbox dispatch

Requirements:

- Persist notification attempts/results in `notifications`, including provider, target, status, payload/rendered body, provider message ID, attempt count, retry time, and error summary.
- Use `outbox_messages` for reliable dispatch of Matrix notifications and other side effects.
- Failed notification sends are retryable and visible in ingestion-run details/admin UI.
- Notification status is linked to the originating operation and ingestion run.

Verification:

- Successful send creates notification audit row with provider message ID.
- Simulated notifier failure creates retryable notification/outbox state without failing the whole ingestion run.
- Retried notification updates attempt count and final status.

## Phase 15: Observability

