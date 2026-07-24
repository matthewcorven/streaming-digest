### Task 12.8: Measure search performance against latency targets

Own and verify the search performance targets declared in this plan (between Phases 11 and 12).

Requirements:

- Generate synthetic representative datasets at approximately 500 and approximately 2,000 videos with proportional segments, transcripts, links, repositories, notes, search documents, and embeddings.
- Measure P50/P95 search latency on both datasets against targets: <= 2s/5s at < 500 videos; <= 3s/10s at 2,000 videos.
- UI shows a spinner or progress state after 1 second.
- Document the measurement environment and how to re-run the benchmark.

Verification:

- Latency targets met on both dataset sizes or documented with a remediation plan.
- UI progress indicator appears after 1 second for slow queries.
- Latency measurement results for both dataset sizes committed as a baseline per the Verification evidence convention (not only the method).

## Phase 13: Notes and edit modals

