### Task 13.3: Implement Blazor modals

Source: `docs/product/PRD.md` §2.5

- Edit modal with tabbed groups of fields.
- Lightweight notes modal opened contextually once an item appears in search results.
- EasyMDE markdown editor if cheap; rich notes UX is not an MVP focus.
- Link-classification correction feedback: `Future similar links will use this correction`, shown on save and when viewing corrected items later.

Verification:

- User can edit metadata and note; search reflects update after embedding regeneration.
- Note boosts parent item ranking.
- Classification correction displays feedback and influences future prompt examples/rules.

## Phase 14: Matrix notifications

