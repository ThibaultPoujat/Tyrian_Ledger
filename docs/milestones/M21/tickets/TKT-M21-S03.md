# TKT-M21-S03 - Implement Mes Signaux MVP and New Primary Navigation

GitHub issue: #131

## Milestone

M21 - Mes Signaux and Crafting Intelligence

## Goal

Deliver the first genuinely usable second-screen `Mes Signaux` experience as soon as possible by reusing the deterministic recommendation/accounting engines already built, without waiting for full Active/Passive plan orchestration or crafting.

## Functional outcome

`Mes Signaux` becomes the default primary surface. It shows only concrete manual actions that merit attention, with compact cards and expandable evidence, plus a compact realized-performance summary and truthful source freshness.

## Requirements

- Implement primary navigation `Mes Signaux / Artisanat / Réglages`.
- `Mes Signaux` is the default/home destination.
- `Artisanat` may be visible but disabled or clearly marked unavailable until implemented; do not add a fake empty craft workspace.
- Remove Dashboard/Scanner/Investments/Personal Learning from primary navigation. Supporting routes/capabilities may remain temporarily reachable for diagnostics until #132.
- Reuse existing backend recommendation, portfolio/risk, historical and personal-evidence authority. React must not reproduce financial formulas.
- Attention-gate the feed to concrete actions supported by current backend evidence.
- Internal no-action states such as WAIT/HOLD/KEEP BID/LEAVE SELL LISTING/SKIP/REVIEW do not appear as ordinary Signals.
- A zero-Signal state says there is currently nothing worth the user's attention rather than manufacturing work.
- Initial Signal content: concrete action, item, quantity/relevant price, modeled profit/result, confidence, and `Pourquoi ?` for deeper evidence.
- Operational status is separate and compact: stale source, ArenaNet unavailable, sync failure, etc.
- Freshness reflects actual relevant data-source timing rather than a fabricated uniform timestamp.
- Show 30-day realized profit as the main performance number, with 7d/90d secondary context. Open/unrealized result stays separate.
- Follow the owner-approved UX decision record from #130.
- All user-facing UI/UX copy introduced or touched by this ticket is French, including labels, actions, messages, errors, empty states, helper text and accessibility text.

## Acceptance criteria

- [ ] App opens on `Mes Signaux` with the new primary navigation.
- [ ] Existing low-value/no-action recommendation states no longer create a wall of user actions.
- [ ] Signal cards expose evidence progressively through `Pourquoi ?`.
- [ ] Zero-signal, degraded-data and stale-data states are understandable and distinct.
- [ ] 30d realized performance is visible without burying Signals; 7d/90d are secondary.
- [ ] French user-facing copy is covered by representative component/E2E tests.
- [ ] Financial truth remains backend-authoritative; no duplicate React recommendation/fee/P&L formulas.
- [ ] Legacy supporting pages are not deleted in this ticket.

## Dependencies

#130, #88, #90.

## Recommended Codex configuration

Risk class: **R2 (cross-layer product/UX)**.

Review path: **NORMAL** unless implementation creates new financial/recommendation authority that warrants explicit escalation.

## Validation

- Focused backend/API tests for action filtering/mapping if changed.
- React component/accessibility tests for Signal, zero-state, stale/degraded-state, performance and navigation states.
- Representative Playwright journey proving default route/navigation and progressive `Pourquoi ?` behavior.
- Verify no browser-side financial formula duplication.

## Non-goals

- Active/Passive path construction or `Démarrer`.
- Local shadow state, `Terminé`, Undo, or API reconciliation.
- Crafting opportunity generation.
- Deleting all superseded UI/code/docs.
- New financial scoring formulas.
