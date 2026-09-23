# TKT-M21-S03 - Implement Signals MVP and New Primary Navigation

GitHub issue: #131

## Milestone

M21 - Signals and Crafting Intelligence

## Goal

Deliver the first genuinely usable second-screen Signals experience, displayed to the user as `Mes Signaux`, as soon as possible by reusing the deterministic recommendation/accounting engines already built, without waiting for full Active/Passive plan orchestration or crafting.

## Functional outcome

The Signals surface displayed as `Mes Signaux` becomes the default primary surface. It shows only concrete manual actions that merit attention, with compact cards and expandable evidence, plus a compact realized-performance summary and truthful source freshness.

## Requirements

- Implement displayed primary navigation `Mes Signaux / Artisanat / Réglages`.
- `Mes Signaux` is the displayed default/home destination for Signals.
- The crafting destination displayed as `Artisanat` may be visible but disabled or clearly marked unavailable until implemented; do not add a fake empty craft workspace.
- Remove Dashboard/Scanner/Investments/Personal Learning from primary navigation. Supporting routes/capabilities may remain temporarily reachable for diagnostics until #132.
- Reuse existing backend recommendation, portfolio/risk, historical and personal-evidence authority. React must not reproduce financial formulas.
- Attention-gate the feed to concrete actions supported by current backend evidence.
- Internal no-action states such as WAIT/HOLD/KEEP BID/LEAVE SELL LISTING/SKIP/REVIEW do not appear as ordinary Signals.
- A zero-Signal state says there is currently nothing worth the user's attention rather than manufacturing work.
- Signal cards follow the owner-reviewed #130 execution-first hierarchy: displayed action, item image/name, quantity, and actionable per-unit price are the easiest information to find; modeled profit/result and qualitative confidence are supporting evidence.
- Trading Post execution fields mirror the in-game entry grammar: quantity first, then per-unit price. Money values render as number + gold/silver/copper denomination glyphs, with accessible text exposing the full value semantically.
- Reserve a stable item-image slot. Use verified item icon metadata when available and a neutral fallback glyph otherwise; missing imagery must not block a Signal or change card geometry. If #131 extends the GW2 item metadata contract, verify/document the upstream field and keep URL construction out of React.
- `Pourquoi ?` expands inline beneath the same card for depth/history/personal/risk evidence; do not use a modal or persistent inspector.
- Operational status is separate and compact: stale source, ArenaNet unavailable, sync failure, etc.
- Freshness reflects actual relevant data-source timing rather than a fabricated uniform timestamp. The compact state names market freshness explicitly; `Données` may disclose `Compte ArenaNet` and `Historique marché` ages.
- Show truthful next-refresh timing only when a scheduler actually exists and knows it. While refreshing show `Actualisation en cours…`. Because the #131 MVP has no continuous Signal scheduler yet, show `Actualisation à la demande`; switch to `Actualisation automatique` only once #95 supplies a real automatic loop. Do not invent a countdown or progress bar.
- Keep the realized-performance block in a stable location across normal, empty, degraded and corrective states. Show `Aujourd'hui` and `30 j` by default; keep `7 jours` and `90 jours` behind a compact disclosure. Open/unrealized result stays separate.
- Validate the primary desktop composition at 1920×1080. Keep the compact left navigation and performance block in stable positions across states.
- Preserve action-type color coding as a fast secondary cue, but never rely on color alone; explicit action text remains authoritative.
- Avoid a decorative rounded footer; prefer no footer unless a flat functional status region is genuinely needed.
- Follow the owner-approved UX decision record from #130 (`docs/ux/signals-second-screen-spike.md`) and the aligned canonical `docs/ux/ux.md`.
- All user-facing UI/UX copy introduced or touched by this ticket is French, including labels, actions, messages, errors, empty states, helper text and accessibility text. Repository/code/internal terminology remains English.

## Acceptance criteria

- [ ] App opens on the Signals surface displayed as `Mes Signaux` with the new primary navigation.
- [ ] Existing low-value/no-action recommendation states no longer create a wall of user actions.
- [ ] At 1920×1080, the compact left navigation and realized-performance block remain stable across normal, zero-signal, degraded and corrective states.
- [ ] Signal cards make the executable instruction easiest to scan: action, item, quantity and Trading Post-style per-unit price precede modeled profit.
- [ ] Gold/silver/copper denomination glyphs are rendered directly with price values and remain accessible; item imagery has a stable fallback.
- [ ] Signal cards expose evidence progressively through inline displayed `Pourquoi ?`.
- [ ] Zero-signal, degraded-data and stale-data states are understandable and distinct.
- [ ] `Aujourd'hui` and `30 j` realized performance are visible without burying Signals; `7 jours`/`90 jours` are behind disclosure.
- [ ] Market/account/history freshness semantics and next-refresh states are truthful and source-specific.
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
- Representative Playwright journey proving default route/navigation and progressive displayed `Pourquoi ?` behavior.
- Verify no browser-side financial formula duplication.

## Non-goals

- Active/Passive path construction or displayed `Démarrer`.
- Local shadow state, displayed `Terminé`, Undo, or API reconciliation.
- Crafting opportunity generation.
- Deleting all superseded UI/code/docs.
- New financial scoring formulas.
