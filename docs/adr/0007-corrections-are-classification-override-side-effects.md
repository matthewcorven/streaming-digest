# Corrections are side effects of classification Overrides, not a separate edit system

The model has both the `*_override` + `field_override_history` system and `classification_corrections`, with API §11 saying a classification edit writes to both. Unspecified: does removing a classification override retract the learning example? Can corrections be edited independently? Two edit histories that can diverge would corrupt the classifier's training signal.

We decided: Override is the only user-edit primitive. A Correction is an Override applied to a classification field whose side effect is appending a learning example to the classifier's few-shot/rule source. Retracting the override (`is_active = false` on the correction) withdraws the example. Corrections are never edited directly.

## Consequences

- `classification_corrections` should link to the override/history entry that spawned it, so retraction is mechanically enforced rather than convention-driven.
- The classifier's few-shot/rule builder reads only active corrections — retracted examples leave the prompt set automatically.
- Override history already captures the audit trail; the corrections table is purely the classifier's training log, not a second history.
- UI copy stays "Future similar links will use this correction" — accurate, since retraction is the only way to withdraw it.
