## MVP scope conformance checklist

Prototype policy (user directive, 2026-07-24): prototypes must use programmatically-created synthetic data so high volume can be generated locally without AI — no embedding/LLM latency, no token cost, and full control over content profile (topic distributions, vocabularies, link/note mixes). Prototypes run as early as possible (user directive, 2026-07-24) — ideally before the implementation they inform — so costly pivots surface while they are still cheap.

Before implementation work is considered MVP-complete, every hard-MVP requirement from `docs/product/PRD.md`, `docs/architecture/ARCHITECTURE.md`, `docs/architecture/DATA_MODEL.md`, `docs/api/API_SPEC.md`, and `docs/operations/UPGRADE_PATHS.md` must be either implemented and verified or explicitly reclassified in the product docs. The implementation plan must not silently pull MVP+ work into MVP. In particular:

- Matrix MVP means unencrypted bot notifications to a configured room. Matrix E2EE, Android/device verification, and E2EE crypto-store readiness are MVP+.
- GitHub repository ingestion is MVP. GitLab, Bitbucket, repository PATs, and repository OAuth are MVP+.
- Link classification correction is MVP. Link-classification search filtering/hide-show behavior is MVP+.
- Recent-search clear-all is MVP. Granular per-query deletion is MVP+.
- Public YouTube channel URL/handle/channel ID input is MVP. Logged-in subscription import, watch-history import, and broader source imports are MVP+.
