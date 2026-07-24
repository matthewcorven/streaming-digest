### Task 13.2: Implement notes APIs

Source: `docs/api/API_SPEC.md` §12; `docs/architecture/DATA_MODEL.md` §3.19 (one note per target in MVP; POST returns 409 Conflict when a live note exists)

CRUD notes for:

- video.
- segment.
- external link.
- repository.

MVP notes are not a primary note-taking product surface. They exist so note content is embedded, evaluated in search weighting, and reflected in the parent video-cluster aggregate.

Verification:

- Note creates search document and embedding.
- Clearing/deleting note updates the note embedding/search document and parent video-cluster aggregate so repeated searches reflect live ranking.
- Deleting a note soft-deletes the note row but hard-deletes its search document and embedding; the stale-documents diagnostics surface never accumulates unresolvable rows from deleted notes.

