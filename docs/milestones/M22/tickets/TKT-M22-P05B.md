# TKT-M22-P05B — Approved Plan Comparison and Execution Preview

GitHub issue: #161

Milestone M22. Risk R2 (interaction state). Luna High explicitly permitted.
NORMAL independent Sol Medium review, including reference/actual image inspection.
Batch B1, fourth and final implementation. Ready after P05A / #160 merges.

## Outcome / scope

Extend P05A's shared application components and explicit fixture-only preview so
the owner can choose between plans, inspect exact instructions and experience
provisional confirmation and recovery states. Reuse the same shell/provider;
no parallel prototype, live backend wiring or financial policy implementation.

OPEN 02-plan-comparison.png and 03-active-plan.png under
docs/ux/prototypes/2026-09-28/, plus the visual-reference semantic overrides.
Use 01 for the shared shell. Keep all original PNGs unchanged.

Entry points: shared components/provider/preview added by P05A; existing
frontend/src/PlanPanel.tsx for live transport semantics only; P01B command contract
and P01C/P01D structured states; preview component tests and Playwright project.

## Comparison

- Approximately 70/30 alternatives/recap layout; 2–3 session-compatible plans,
  highlighted recommendation, clear differing capital/time/profit/cash certainty.
- Selecting an alternative changes the right recap and phase summary without
  reserving resources. Plans may share resources; they are alternatives.
- Démarrer ce plan is the single explicit simulated start. Provider returns a
  new execution or structured unavailable/conflict result. Repeated in-flight
  clicks cannot create several preview executions.
- Both active-time and liquid-gold deadline fixtures must be understandable;
  uncertain future sale cannot masquerade as deadline-qualified liquid cash.
- No feasible plan, insufficient capital and stale evidence have usable French
  reasons and a route back to preferences. No arbitrary relaxation.

## Active plan and recovery

- Approximately 24/47/29 phases/instruction/recap layout. Vertical grouped phases
  scale to a deterministic 20+ step plan with clear scrolling and current focus.
- Exact item, quantity, unit/total price when relevant, actor/location, consumed
  resources and next action. Values are read-only; no quantity or price form.
- API-observed completion is the normal simulated path. Exceptional
  J’ai effectué cette étape sends one intent with original execution/step/revision
  identity to the fixture provider; show Déclaré effectué · vérification en attente.
- Provider supplies pending, confirmed, safe-continuation, waiting-for-required-
  evidence, partial/recheck, contradiction and undone states. UI renders them;
  it does not infer success from a timer, calculate shadow balances or implement
  a competing reconciler. Safe continuation must not impose a fixed five-minute
  delay. Required waits show an honest reason/estimate.
- For lost acknowledgement, reuse P01B interaction semantics: explicit Actualiser
  checks simulated scope then retries original pending identity. New step action
  is distinct; account switch invalidates old pending intents. Tests use the
  provider boundary, not a second production command engine.
- Pausing affects guidance only, never claims to cancel an in-game order.
  Undo/help/resume callbacks have deterministic fixture outcomes; disable actions
  forbidden by provider eligibility with an explanation. Never restore a stale
  instruction after acknowledgement.
- No green protections badge without complete fixture coverage. Cash released,
  modeled profit, realized result, active time and waiting time remain distinct.
  Bilan/settings stay P05A placeholders; those screens have later work units.

## Required evidence

Paired actual 1920×1080 screenshots: comparison, alternate selected, no qualifying
plan, normal active plan, provisional-safe-next, mandatory wait, partial/contradiction,
and long grouped plan. Shared-shell density/hierarchy must follow references.
Keyboard choice/start/navigation, current-step focus, French accessible names,
copy-name behavior, disabled actions and zoom readability are tested.

Provider/network tests establish preview-only actions and absence of production
mutations. Repeated clicks and old acknowledgements cannot move the wrong step.
Record screenshot scenario/route/viewport/device scale/head, paired reference
links, intentional semantic deltas and independent reviewer evidence.

Run frontend tests/build, scoped Playwright preview tests (in required CI),
node --test .github/scripts/*.test.mjs and full required CI. Do not replace
semantic/state tests with screenshot baselines; do not blindly accept image diffs.

Prefix [TKT-M22-P05B]; PR to develop, Closes #161, milestone 11.
Use $tyrian-pr-review with independent Sol Medium; missing visual/review evidence
means Draft. Record batch evidence and stop for owner merge.

## Batch exit

After this merge the next queue item is #150: stop coding and request the B1
checkpoint. Owner reviews the clickable four-screen flow; Astra checks integrated
evidence and prepares the next 3–5 contracts. This is not P01/P05 completion, and
#150 remains open. Runtime settlement/passive continuation, complete account data,
live UI adapters, Bilan/settings and Windows operation remain in the delivery map.
