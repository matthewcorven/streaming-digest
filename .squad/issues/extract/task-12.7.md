### Task 12.7: Build search recall evaluation harness

`docs/architecture/ARCHITECTURE.md` §4.10 sets a hard recall target: the intended recalled video cluster appears in the top 3 results for the representative vague-query corpus. Fixed-data ordering tests in 12.3/12.5 validate the formula, not recall.

Requirements:

- Golden dataset of at least 20 vague/natural-language queries mapped to expected video clusters (from the Task 0.4 fixture corpus), authored query-first — each query written before its fixture video's metadata is finalized, so queries cannot be reverse-engineered from the text.
- The gate runs against a corpus padded to the MVP scale assumption (~500 videos): golden fixture videos are the recall targets and synthetic, topically-adjacent distractor videos from the Task 12.8 dataset generator fill the rest (ADR-0013). The gate is 100% top-3 recall on the dataset — top 3 of ~500 — no partial pass; regressions are fixed in ranking/weights/document construction, never by editing the dataset to fit.
- Dataset growth in MVP is file-based: failed real queries are added to the golden dataset by editing the dataset file with the query, expected video, and provenance notes. An in-UI "this should have ranked" capture/review queue is MVP+.
- Automated integration test asserts each expected cluster appears in the top 3 results.
- Harness re-runs whenever the ranking formula version, embedding provider/model/dimensions, or search-document construction changes.
- Recall regressions are reported per query with score components to aid diagnosis.

Verification:

- Golden dataset meets the top-3 recall target on the ~500-video representative corpus.
- Harness fails when a ranking/model change drops an expected cluster out of the top 3.
- Recall harness run reports (per-query score components) committed per the Verification evidence convention.

