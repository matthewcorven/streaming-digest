### Task 2.4: Implement first-run onboarding state

Requirements:

- Start in onboarding if setup is incomplete.
- Required before first ingestion: admin password setup/change, embedding model verification, local LLM verification, first public YouTube channel, and ingestion schedule confirmation.
- Default ingestion schedule is 6 AM local user time and configurable during first run.
- Audio-to-text, Matrix, and Grafana/observability verification contribute to full readiness but surface warnings instead of blocking basic search UI access.
- Each setup step supports live verification, inline retry, retained previous values, clear success state, and actionable failure messages.
- Post-login routing precedence: incomplete onboarding, last selected mode, dashboard summary after first daily run, then ingestion/new-videos digest.

Verification:

- Incomplete setup routes to onboarding.
- Core-value setup can proceed while Matrix/Grafana/Whisper warnings remain visible.
- Verified settings persist and pre-fill on retry.

