### Task 11.3b: Prototype vector user-search approach

Source: `docs/architecture/DATA_MODEL.md` §6; `docs/architecture/ARCHITECTURE.md` §4.5; ADR-0012, ADR-0013; `.agents/skills/prototype/SKILL.md` (logic branch)

Throwaway prototype validating the query-side vector search and ranking approach before production implementation, using synthetic data per the prototype policy in the MVP scope conformance checklist.

Requirements:

- Programmatic synthetic query generator (paraphrase templates, vague-query patterns, topic sampling from the corpus vocabulary, no AI) producing natural-language-style queries with known expected video-cluster mappings.
- Validates against the Task 11.3a corpus/embeddings: hybrid document scoring (`textWeight`/`vectorWeight` blend), cluster-score aggregation formula, `relativeSimilarityPercent` normalization over the pre-pagination candidate set, recent-search embedding storage and high-signal absolute-cosine matching against the threshold (ADR-0012), and coarse related-item discovery via cluster aggregates.
- Explores ranking weight ranges and coverage/note/interaction boost behavior to give Task 12.3 an evidence-based starting point rather than untested constants.
- Output: findings on scoring/ranking behavior, recommended default weight ranges, and any formula corrections; if the outcome changes the ranking formula, record an ADR (`docs/adr/`, next available number).

Verification:

- Synthetic queries execute against the prototype corpus with zero external AI calls.
- Comparison report committed per the Verification evidence convention; any resulting ADR recorded.

