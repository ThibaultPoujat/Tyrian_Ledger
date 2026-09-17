# TKT-M21-S05 - Implement Signal Plan Orchestration, Active/Passive Paths, and Shadow Reconciliation

GitHub issue: #133

## Milestone

M21 - Signals and Crafting Intelligence

## Goal

Turn independent opportunities into stable, mutually compatible execution plans that fit the user's current attention and resources, then keep the assistant responsive while manual actions await ArenaNet confirmation.

## Core model

`Opportunity -> Plan -> Steps -> Signal -> Reconciliation -> Outcome`

## Requirements

- Convert surviving opportunities into deterministic executable plans with explicit resources, modeled profit, confidence, urgency, interaction time and dependencies.
- Apply hard validity/safety/evidence/liquidity/risk constraints before ranking; score can never override them.
- Select mutually compatible bundles rather than simply taking the top-N opportunities.
- Reserve cash, inventory, current orders/exposure, expected incoming materials and other shared resources so two plans cannot spend/use the same resource.
- Support Passive and Active proposals as attention/time-oriented choices, not persistent modes.
- Passive favors low interaction and capital turnover; Active favors immediately executable chains/profit per attention time.
- A buy-order step may appear in an Active chain only when terminal and non-blocking.
- Preserve hard bankroll reserve and a bounded opportunity-capital buffer; soft attractiveness thresholds may relax toward, never below, hard floors when meaningful capital remains idle.
- Rank at plan/bundle level using deterministic confidence-adjusted economics, capital efficiency/turnover, attention fit, urgency, concentration/opportunity cost and stability.
- Use hysteresis: a started plan survives small score changes and is replaced only after material improvement or invalidation under explicit versioned policy thresholds.
- The displayed `Démarrer` action reserves the selected plan's resources and recomputes alternatives from what remains.
- Freeze the current instruction during execution. Material invalidation produces a French recheck state such as `Recalcul requis` rather than silently changing quantity/price.

## Local execution shadow

- Effective planning state = latest verified ArenaNet state + locally recorded unconfirmed execution events.
- The displayed `Terminé` action records that the current instruction was performed essentially as issued and advances projected state immediately.
- Exceptional flow allows different executed quantity/price instead of forcing false exact evidence.
- Unconfirmed local events are reversible; normal UX supports at least the displayed `Annuler la dernière étape` action.
- Reversing an earlier event invalidates/reconciles dependent later local events.
- Store local execution events separately from verified state; prefer append-only event + reversal semantics.
- API refresh may confirm completed shadow events while an Active path continues, without silently mutating the current frozen instruction for immaterial changes.
- Exact/compatible API confirmation retires corresponding shadow effects without double counting.
- Not-yet-observable actions remain provisional only within bounded endpoint-aware expectations.
- Material contradiction between local report and repeated verified evidence pauses the plan with a French reconciliation state rather than guessing.
- Verified ArenaNet state eventually wins.

## Acceptance criteria

- [ ] Resource-conflicting candidates cannot be selected simultaneously.
- [ ] Bundle selection can prefer a compatible combination over one higher-absolute-profit candidate when deterministic utility supports it.
- [ ] Passive/Active proposals fit their attention semantics and recompute after the displayed `Démarrer` action.
- [ ] Started plans remain stable through immaterial market changes.
- [ ] Each completed step immediately changes effective planning state without waiting for API refresh.
- [ ] Undo restores projected resources and handles dependent later steps safely.
- [ ] API refresh during an Active path can confirm prior steps without unexpectedly mutating the current instruction.
- [ ] Material contradiction enters explicit reconciliation rather than silently trusting either projection.
- [ ] All user-facing copy introduced/touched is French.
- [ ] Deterministic tests cover bundle/resource conflicts, reserves, urgency, hysteresis, shadow projection, confirmation, undo, partial-compatible evidence and contradiction.

## Dependencies

#132, #87, #88, #90.

## Recommended Codex configuration

Risk class: **R3 (recommendation/state/accounting-adjacent authority)**.

Review path: **SOL-GATED** — Terra High implementation/fixes, Draft PR, required validation/CI green before fresh separate Sol XHigh review, targeted fresh Sol re-review after scoped fixes.

## Non-goals

- Trading Post/game automation.
- Opaque/ML optimization.
- Crafting-specific recipe search; crafting plugs into this plan model later.
