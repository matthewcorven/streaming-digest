# Repository owns metadata; linked Resources delegate to it

The model stores canonical repo URLs in both `repositories` and `external_resources` (`resource_type = 'repository'`), joined by `external_resource_repositories`. With override fields on both tables, nothing said which one feeds search documents and result cards, so the two rows could drift into showing different titles/descriptions for the same repo.

We decided: the Repository is the single source for repo metadata (description, stars, language, README, DeepWiki) and its overrides. A Resource classified `code_repository` keeps only classification and scrape status; its title/description Effective Values delegate to the linked Repository. The Resource→Repository association is created eagerly at classification; the Repository row materializes when the metadata stage first succeeds.

## Consequences

- Repo metadata overrides live on the Repository only; the Resource's metadata override fields for classified repos are ignored for display/search (schema keeps them for uniformity).
- Result cards render Repository data when the association exists, Resource data (classification + "metadata unavailable" warning) only when it doesn't.
- Deletion semantics per DATA_MODEL §9 are unaffected: deleting a channel removes occurrences/associations; a shared Repository row survives while other videos still link it.
- `DATA_MODEL.md` §3.13/§3.15 and `API_SPEC.md` §11 (override endpoints) should state the delegation rule explicitly.
