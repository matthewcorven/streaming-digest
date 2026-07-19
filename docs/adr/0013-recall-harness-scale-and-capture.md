# Recall harness runs at MVP corpus scale and grows via in-UI capture

The recall gate (Task 12.7) promises the killer journey: the intended video cluster appears in the top 3 for vague queries. Running that gate against only the 30–60 golden fixture videos makes top-3 nearly free — there are too few competitors — so the gate could pass in CI and fail in the real ~500-video deployment. Separately, "the dataset grows whenever a real user query fails" had no capture mechanism: remembering the query, finding the video by other means, and hand-writing an entry is enough friction that the dataset would never grow.

We decided:

- The recall harness pads the corpus to the MVP scale assumption (~500 videos): golden fixture videos are the recall targets, and the remaining videos are synthetic, topically-adjacent distractors produced by the same corpus generator used for the Task 12.8 latency datasets. The gate remains 100% top-3 on the golden queries — now meaning top 3 of ~500.
- A "this should have ranked" affordance in the search UI captures a failed real query as a candidate golden entry — query text, expected video, score components, and ranking formula version — into a review queue. Promotion into the golden dataset is a deliberate user act, keeping the gate fair (no typo'd or unreasonable queries poisoning 100%).

## Considered options

- Golden-fixture-only corpus: simplest, but the gate measures a corpus an order of magnitude smaller than the product promises.
- Two-tier harness (fast small gate in CI + slow 500-video gate nightly): catches obvious regressions early, at the cost of two configurations to keep honest; a single scale-true gate was preferred.
- Auto-capture without review (any opened-below-rank-3 result becomes golden): zero friction, but junk queries make the 100% gate fail for non-ranking reasons.

## Consequences

- The Task 12.8 synthetic dataset generator becomes a shared dependency of the recall harness and must support deterministic distractor generation alongside golden fixtures.
- A new candidate-queue store (table or fixture-side file) holds captured failures with their diagnostics; the review surface is part of Admin/Settings.
- Captured score components double as the diagnostic payload when a ranking change regresses recall.
- The golden dataset's query-first authorship rule (CONTEXT.md) is unchanged; captured real queries enter through the same review bar.
