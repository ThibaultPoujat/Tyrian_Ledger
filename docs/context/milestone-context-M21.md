# Milestone Context - M21: Signals and Crafting Intelligence

## User outcome

The owner gets a usable second-screen profit assistant before crafting is complete, then gains stable Active/Passive execution plans and guided crafting-for-profit paths built on the same deterministic resource/reconciliation model.

## Product sequence

M21 contains two linked phases after the completed account-crafting ingestion ticket:

1. **Signals transition** — self-healing delivery state, UX spike, attention-first MVP/navigation, post-MVP cleanup, then shared plan/shadow/reconciliation orchestration. The displayed primary surface is `Mes Signaux`.
2. **Crafting intelligence** — owned-material opportunity-cost economics followed by bounded guided crafting paths and the workspace displayed as `Artisanat`.

The explicit ticket order is defined in `docs/milestones/INDEX.md` and `CURRENT.md`; do not infer it from numeric issue ordering.

## Signals invariants

- The Signals surface displayed as `Mes Signaux` is the primary daily surface.
- Repository-facing artifacts and internal terminology are English; user-facing UI/UX copy is French.
- A Signal appears only when there is a concrete manual action worth the user's attention.
- No-action analytical states remain internal unless they imply a corrective manual action.
- Dashboard/scanner/personal-learning/raw inventory are supporting engines/evidence, not competing primary destinations.
- Displayed target navigation is `Mes Signaux / Artisanat / Réglages`.
- Financial/recommendation truth remains backend-authoritative and deterministic.
- Active/Passive are attention/time-oriented paths, not permanent modes.
- Plans reserve shared resources and must be mutually compatible.
- Local execution shadow is reversible provisional evidence; verified ArenaNet state eventually wins.

## Crafting invariants

Owned tradable materials are not free. Bound/unknown inputs remain explicit. Search is bounded, cycle-safe and memoized. Procurement compares owned opportunity cost, instant acquisition, waiting/buy-order acquisition and recursively crafted intermediates where supported. A theoretical craft margin without output liquidity/history/confidence does not become an actionable Signal.

`Mixed procurement` is an internal optimization result, not a user-selected mode. If selling raw owned inputs is economically superior, crafting must not be recommended merely because gross output price is high.

## Reuse

The historical M5 bounded recipe-graph design remains useful reference material where compatible. M19/M20 recommendation, sizing, history and personal-evidence engines remain reusable inputs to the new product shape; do not duplicate them in React.

## Review

- S01-S04 use NORMAL review under their risk class.
- S05 / #133 is SOL-GATED because it owns recommendation/resource/shadow/reconciliation authority.
- TKT-M21-02 / #93 and TKT-M21-03 / #94 remain SOL-GATED financial/algorithmic work.