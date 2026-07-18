# Channel Degraded is a stored state with probe-based recovery

ARCHITECTURE §4.8 said repeated adapter failures "mark the channel degraded until a future daily run succeeds without failures," but gave no lifecycle: if a Degraded channel is skipped, how does it ever get a chance to succeed? It was also unclear how Degraded interacts with rate-limit Deferments (§4.9), which live in a separate table.

We decided Degraded is a stored channel-level state with explicit transitions, orthogonal to Deferment:

- Entered after two consecutive runs fail at the adapter stage.
- While Degraded, the channel is skipped by scheduled ingestion, but each scheduled run performs a single lightweight probe (one metadata fetch). A successful probe clears Degraded and the channel rejoins the run; a failed probe increments the failure count.
- An active Deferment pauses the failure counter — failures during a deferment don't count, because the channel never had a fair chance.
- The user can manually clear Degraded; this only resets the counter, so the next run re-trips it if the underlying problem persists.

## Consequences

- `channels` needs columns for degraded state, consecutive-failure count, and last-probe time (a schema addition to DATA_MODEL §3.5).
- Degraded appears on the dashboard and pending-action inbox alongside Deferments, but the two are never merged: Deferment is host-scoped and time-bound; Degraded is channel-scoped and survives deferment expiry.
- Probe traffic is negligible (one metadata fetch per degraded channel per run) but gives automatic recovery without user intervention for transient failures.
