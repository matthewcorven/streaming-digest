# Digest is a stored, run-scoped artifact with two renderings

The docs described the daily digest in three places — dashboard section, Matrix notification, post-login routing — without saying whether they share one source of truth or are assembled independently. Independently assembled surfaces would inevitably diverge ("Matrix said 3 new videos, dashboard says 4").

We decided: when an ingestion run completes, a Digest record is assembled and persisted (counts, new items, high-signal matches, failures, deferments) and linked to the run. The dashboard digest section renders the most recent stored Digest; the Matrix notification is a rendered excerpt of the same record. One assembly, two renderings.

## Consequences

- Matrix and dashboard can never disagree about a run's outcome; retrying a notification re-renders the same stored payload.
- The digest needs a persistence home: a `digests` table or a well-defined `digest_json` payload on `ingestion_runs`/`notifications` (schema addition to DATA_MODEL).
- Post-login routing to "dashboard summary after first daily run" becomes simply "render the latest Digest."
- High-signal matching runs once, at digest assembly time, against recent-search embeddings as of that moment — late searches don't retro-edit a digest.
- Rolling windows and "since you last looked" semantics are explicitly MVP+.

## Amendment: dashboard renders the deferments subsection live

A Deferment created hours after a run completes appears immediately in the live Pending-Action Inbox, but the stored Digest would show "no active deferments" until the next run — the dashboard's two sections contradict each other for up to a day. We decided the dashboard's Digest section re-derives the deferments subsection from live deferment state at render time. The stored Digest record is untouched (ADR-0005 immutability stands), and the Matrix rendering keeps the stored snapshot — a message is honest as a point-in-time artifact, while a dashboard is a live surface.

The exception is deliberately narrow: deferments are the one Digest field whose truth decays by the hour and whose purpose is "act on this now." All other fields (new videos, failures, high-signal matches) are historical facts about the run and stay stored. If a second field ever seems to need live treatment, that is a signal the field belongs in the Pending-Action Inbox rather than the Digest.
