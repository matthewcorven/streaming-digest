### Task 0.5: Wire baseline observability instrumentation

Wire OpenTelemetry and structured logging in API and worker during initial solution setup rather than retrofitting in Phase 15.

Requirements:

- OTLP export to Aspire dashboard locally and to the OTel Collector endpoint when configured.
- Baseline instrumentation for HTTP requests, EF Core/Db calls, and Hangfire jobs.
- Correlation/trace ID propagation helpers that ingestion stages and adapters adopt as they are built.
- Serilog or `Microsoft.Extensions.Logging` structured logging configured in both hosts.

Verification:

- Local Aspire dashboard shows traces/logs/metrics for a smoke API request and test job.
- Every later phase's service code emits traces without additional plumbing work.

## Phase 1: Database foundation

