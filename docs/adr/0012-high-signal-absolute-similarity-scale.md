# High-Signal matching uses absolute cosine similarity, not rank-relative percentage

CONTEXT.md originally claimed High-Signal Matches use "the same fingerprints and similarity scale as Related Items, so percentages are comparable across both surfaces." But `relativeSimilarityPercent` is normalized within the pre-pagination candidate set — a rank-relative score whose range stretches or compresses with corpus composition. A threshold of 80% on a rank-relative scale is not a bar: its meaning drifts as the corpus grows, and "no high-signal matches" becomes ambiguous between "nothing matched" and "the scale moved."

We decided High-Signal Match evaluation uses raw cosine similarity between the new item's cluster fingerprint and the recent-search embedding — a fixed, absolute bar the user can reason about and configure. Related Items keep rank-relative percentages, because they answer the browse question "what else is like this?" where only ordering matters.

## Considered options

- Rank-relative for both surfaces: one scale everywhere, but the subscription threshold silently changes meaning with corpus composition.
- Percentile-based threshold ("top N% of this run's candidates"): self-calibrating, but opaque as a user setting and still not comparable across runs.

## Consequences

- CONTEXT.md's cross-surface comparability sentence is corrected: the two surfaces share fingerprints but deliberately use different scales, and their percentages are not comparable.
- `search.highSignalThresholdPercent` (default 70 after Issue #100 calibration) is interpreted against absolute cosine similarity; the digest UI should label it as such to avoid confusion with `Relative similarity` tooltips.
- Digest high-signal rows show the absolute percentage; API/docs must not describe it as "relative similarity."
- The daily-digest high-signal gate is now stable across corpus growth, embedding-space changes are still handled by the Embedding Transition skip rules (ADR-0008, ADR-0011).
