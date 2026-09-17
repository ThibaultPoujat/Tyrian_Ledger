# TKT-M21-S02 - Prototype and Validate Mes Signaux Second-Screen UX

GitHub issue: #130

## Milestone

M21 - Mes Signaux and Crafting Intelligence

## Goal

Validate the `Mes Signaux` second-screen experience before implementing the MVP, so the implementation ticket receives an intentional UX target rather than inventing one while coding.

## Scenarios to prototype

- zero signals / nothing worth doing;
- two simple actionable signals;
- many competing candidates with only the attention-worthy subset shown;
- fresh market data with older account data and vice versa;
- degraded/sync-failed operational state;
- urgent corrective action;
- expanded `Pourquoi ?` evidence;
- compact 30-day realized-profit summary with 7d/90d secondary context;
- target primary navigation `Mes Signaux / Artisanat / Réglages`.

## UX rules

- All user-facing labels, actions, messages, errors, explanations, accessibility text, and empty/degraded states are in French.
- Initial Signal cards show only concrete action, item, quantity/price, modeled result/confidence, and `Pourquoi ?`.
- Internal `WAIT`, `HOLD`, `KEEP BID`, `REVIEW`, etc. are not presented as user interruptions.
- Data freshness reflects the actual relevant source, not a fake single refresh age.
- Desktop/second-screen density is the primary target.
- `Artisanat` may be visible but disabled/clearly unavailable until implemented; `Mes Signaux` is the default destination.

## Tooling

ChatGPT Sites is a preferred rapid-prototyping tool if/when available in the owner's region, but availability must not block this ticket. A lightweight local/mock prototype or documented wireframe is sufficient.

## Deliverables

- Prototype/wireframe covering the scenarios above.
- Short durable UX decision record under `docs/ux/`.
- No new financial/recommendation logic.

## Acceptance criteria

- [ ] Owner can review the principal states before MVP implementation.
- [ ] Card hierarchy, navigation, empty/degraded states, performance placement, freshness display, and `Pourquoi ?` behavior are documented.
- [ ] French UI copy is represented in the prototype.
- [ ] No dependency on live ArenaNet/API integration is required.

## Dependencies

#129.

## Recommended Codex configuration

Risk class: **R1 (product/UX spike)**.

Review path: **NORMAL** — Terra Medium by default, escalating if the spike uncovers cross-layer constraints.

## Non-goals

- Production React implementation.
- Full Active/Passive interaction design.
- Shadow-state persistence/reconciliation implementation.
