### Task 15.1: Verify and extend OpenTelemetry instrumentation

Baseline instrumentation (OTLP export, HTTP/DB/Hangfire instrumentation, correlation helpers, structured logging) is wired in Task 0.5. This task verifies and extends coverage to all remaining signal sources.

Instrument/verify:

- API requests.
- DB calls.
- Hangfire jobs.
- ingestion stages.
- embedding calls.
- LLM calls.
- audio-to-text calls.
- scraper calls.
- Matrix sends.

Verification:

- Local Aspire dashboard shows traces/logs/metrics for each signal source above.
- No service required late retrofit of baseline plumbing to emit traces.
- Automated smoke assertions: an integration test emitting an API request and a test Hangfire job asserts trace spans and structured log fields (including correlation ID) are produced through the OTLP pipeline — not only manual dashboard checks.

