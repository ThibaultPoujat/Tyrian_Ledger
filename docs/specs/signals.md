# Signals — Second-Screen Profit Assistant Specification

## 1. Product purpose

Tyrian Ledger is not primarily a market-analysis application. It is a
**local-first second-screen Guild Wars 2 profit assistant** that continuously
turns market/account intelligence into the smallest useful set of concrete
manual actions for the owner to perform in game.

Expanded product statement:

> Tyrian Ledger continuously observes the owner's GW2 economy state and market
> evidence, identifies the best risk-controlled ways to increase wealth,
> constructs mutually compatible execution plans, and shows only the concrete
> actions that should be performed now. It minimizes time spent analysing
> markets so the owner can act quickly while continuing to play the game.

The 0.1 economic focus is Trading Post flipping/trading and crafting. Existing
investment-position/staged-exit infrastructure remains valid code and data, but
investment opportunity discovery and seasonal speculation are deferred.

## 2. Repository language versus displayed language

Repository-facing artifacts are written and named in **English**. This includes:

- documentation filenames and ordinary documentation prose;
- code, type, API, database and migration identifiers;
- internal domain terms, enums, reason codes and configuration keys;
- ticket-internal technical terminology and test identifiers.

French is the **display language of the product UI**. Exact French strings may be
quoted in documentation when they represent what the user sees.

All user-facing UI/UX labels and text are French, including navigation, actions,
buttons, headings, helper text, errors, empty/degraded states, notifications,
explanations and accessibility text. Proper nouns and technical identifiers may
remain canonical where translation would reduce clarity.

Financial truth and business logic never depend on localized strings. Backend
contracts expose structured semantics/reason identifiers; the presentation layer
renders them in French.

## 3. Primary surface and navigation

The primary daily **Signals** surface is displayed to the user as **`Mes Signaux`**.

Target displayed primary navigation:

- `Mes Signaux` — what manual action is worth performing now;
- `Artisanat` — deliberate guided crafting-for-profit workspace;
- `Réglages` — API/account, refresh/health, risk/bankroll policy, alerts,
  backup/recovery, data health and advanced diagnostics.

Dashboard, scanner, raw inventory, personal-learning tables, order-book detail
and raw history remain supporting engines/evidence. They are not competing
primary destinations. Diagnostic access may remain where useful, but the normal
user should not need to operate those engines directly.

`Mes Signaux` is the default/home destination. Until the crafting workspace is
implemented, `Artisanat` may be visible but disabled/clearly unavailable rather
than opening a fake empty workspace.

## 4. Canonical internal vocabulary

Tyrian Ledger distinguishes these English internal concepts:

`Intelligence -> Opportunity -> Plan -> Steps -> Signal -> Reconciliation -> Outcome`

### Intelligence

Raw and derived market/account evidence normalized into usable decision
intelligence: current market, order book, owned history, account state, current
orders, inventory, crafting capability, personal turnover/performance evidence,
freshness and risk state.

### Opportunity

A theoretically useful economic possibility discovered from intelligence. An
opportunity is not automatically something the user should see.

### Plan

A deterministic proposed execution strategy for one opportunity or a compatible
bundle of opportunities. It states required capital/resources, expected modeled
result, confidence, urgency, interaction time, dependencies and constraints.

### Step

One concrete manual action in a plan, such as placing/updating/cancelling an
order, buying now, crafting, listing, relisting or selling.

### Signal

> **A Signal is an opportunity sufficiently safe, profitable, relevant and
> compatible with the owner's current state to justify a concrete manual
> action.**

A good market observation is not necessarily a Signal. It becomes one only
after evidence, portfolio/resource constraints, freshness, conflicts and
attention rules have been applied.

### Reconciliation

The deterministic process that compares provisional locally reported execution
with later ArenaNet/account evidence and updates effective state without double
counting or guessing.

### Outcome

The observed economic result of an executed plan where enough evidence exists.
Unexecuted suggestions remain unobserved rather than receiving fabricated
counterfactual profit.

## 5. Attention gate — silence is a feature

If something appears in the normal `Mes Signaux` action feed, it is there because
the owner should act.

Conceptual eligibility sequence:

1. relevant intelligence changes or a fresh planning pass occurs;
2. determine whether the user actually needs to do anything;
3. require a precise manual action;
4. require compatibility with capital, inventory, exposure, risk and freshness;
5. only then make the action eligible for the Signals surface.

Backend engines may retain internal states such as `WAIT`, `KEEP BID`, `HOLD`,
`SKIP`, `REVIEW`, `LEAVE SELL LISTING`, harmless undercut/outbid, insufficient
history, an unprofitable recipe, or matured evidence. These normally produce
**silence** on the main action feed.

An internal state may become visible when it implies a concrete corrective
manual action, for example cancelling a buy order because committed capital is
now economically invalid or violates reserve/risk policy.

Operational health is separate from profit Signals. A compact status area may
show stale data, ArenaNet unavailable, sync failure, missing permission or
similar truth without pretending those are profit opportunities.

## 6. User-facing action vocabulary

Internal action/reason identifiers remain English and structured. Their displayed
French copy should converge on concrete hand actions such as:

- `ACHETER MAINTENANT`;
- `PLACER UN ORDRE D'ACHAT`;
- `METTRE À JOUR L'ORDRE D'ACHAT`;
- `ANNULER L'ORDRE D'ACHAT`;
- `FABRIQUER`;
- `METTRE EN VENTE`;
- `RE-LISTER`;
- `VENDRE MAINTENANT`;
- `VENDRE PARTIELLEMENT`.

Exact final copy is a UX decision; semantics remain backend-authoritative.

## 7. Signal-card information hierarchy

The default card is intentionally small. It makes these scannable first:

- concrete action;
- item;
- quantity;
- relevant price/maximum price where applicable;
- modeled profit/result;
- confidence;
- `Pourquoi ?` progressive disclosure.

Detailed ROI, spread, order-book depth, retained-history components, personal
evidence, anomaly flags, portfolio constraints and calculation assumptions sit
behind `Pourquoi ?` unless they are themselves the reason the instruction must
change.

Data age/freshness reflects the actual relevant source. The UI must not invent a
single fake global age when endpoints have different cache/refresh semantics.

## 8. MVP before full plan execution

The first usable Signals MVP deliberately arrives before the full plan engine. It
reuses existing recommendation/accounting/history/personal-ranking engines and:

- makes `Mes Signaux` the home page;
- introduces displayed navigation `Mes Signaux / Artisanat / Réglages`;
- applies the attention gate to hide no-action noise;
- uses minimal cards plus `Pourquoi ?`;
- displays truthful operational freshness/health;
- shows compact realized performance;
- provides a useful zero-Signal state;
- provides a functional `Réglages` destination by grouping/reusing existing
  account/status/data/risk controls rather than adding a dead placeholder.

It does **not** yet implement Active/Passive path construction, `Démarrer`,
`Terminé`, Undo or local execution shadow state. Those belong to the shared plan
engine.

## 9. Opportunity-to-plan selection

Do not simply sort independent candidates and take the top N.

Planning pipeline:

`all opportunities -> hard rejection -> executable plans -> attention classification -> shared-resource conflict resolution -> bounded compatible-bundle optimization -> Signals`

### Hard rejection first

A score can never compensate for failure of a hard requirement. Examples:

- unsafe or incomplete financial evidence;
- evidence/liquidity below a hard floor;
- impossible quantity;
- stale evidence beyond permitted policy;
- unresolved state contradiction;
- portfolio/risk violation;
- insufficient resources;
- conflicting use of the same owned inventory/capital.

### Plan-level utility

Surviving plans are compared deterministically using named evidence including:

- confidence-adjusted modeled economic return;
- capital efficiency and expected turnover;
- attention/time compatibility;
- urgency/time sensitivity;
- concentration and opportunity cost;
- plan stability.

Raw gold profit alone and raw ROI alone are insufficient. A time-sensitive plan
may outrank a slightly more profitable non-urgent plan when the versioned utility
policy supports it.

### Bounded portfolio selection

Compatible-bundle selection is a **bounded deterministic portfolio-selection
problem**, not simple sorting and not unbounded world optimization. Candidate
counts/search breadth must be explicitly bounded and tested so the result remains
predictable and explainable.

The planner reasons globally about shared resources. Two plans cannot both spend
the same cash, consume the same owned inputs, assume the same sellable inventory,
or ignore exposure already committed to current orders/positions.

A compatible combination of smaller plans may be preferable to one larger plan
when deterministic utility/resource calculations support it.

## 10. Hard reserve, opportunity reserve and idle capital

The existing hard bankroll reserve remains a safety constraint (reference policy
currently approximately 15%). It is never consumed simply because few
opportunities exist.

Inside otherwise deployable capital, the planner may preserve a softer
**opportunity reserve** so mediocre current ideas do not consume all optionality
before a stronger opportunity appears.

The opportunity reserve may vary deterministically with opportunity quality,
urgency, expected turnover, idle capital and the available candidate set. It is
not a second hidden safety rule and **must not be implemented as a fixed 75–85%
commitment target**; earlier percentage examples were illustrative only.

Use two conceptual thresholds:

- **hard floor** — correctness/evidence/liquidity/risk requirements that never
  relax;
- **preferred attractiveness floor** — the level normally worth interrupting the
  user for.

If meaningful deployable capital remains idle, the preferred floor may relax
toward the hard floor. The hard floor never relaxes. An action must still clear
an absolute value/attention threshold; trivial profit is not useful merely
because it is safe.

## 11. Passive and Active execution paths

Passive and Active describe **attention/time orientation**, not different
investment philosophies and not permanent session modes.

### Passive

Typical interaction budget is approximately 1–3 minutes. Prefer:

- place/update/cancel buy orders;
- listings and other low-interaction actions;
- strong profit/capital turnover with little immediate attention;
- actions that can wait for market fills while the owner returns to gameplay.

A started Passive plan may remain in progress while waiting on the market. Active
work may use only resources that are not reserved by that Passive plan.

### Active

Typical interaction budget is approximately 5–15 minutes for one path, while the
owner may perform successive Active paths for much longer overall. Prefer:

- immediately executable acquisitions;
- crafting/intermediate chains;
- listings/sales;
- urgency and profit per minute of active attention.

A buy-order step may occur inside an Active path only as the terminal step. It
must not block later immediate steps in the same path waiting for a fill.

Before selection, displayed Passive and Active proposals may be alternative
hypothetical bundles and therefore may overlap resources. After `Démarrer`, the
selected plan reserves its resources and all alternatives are immediately
recomputed from what remains.

When there are only a few obvious independent actions, the UI may show one
ordered action list rather than manufacturing unnecessary Passive/Active
ceremony.

A future optional displayed time-budget control such as `2 min / 10 min / 20+
min` may shape Active-plan construction. It is an input to planning, not a
persistent operating mode.

## 12. Plan order, explanation and stability

### Step ordering inside a selected bundle

When multiple executable actions belong to a bundle, order them deterministically
using this conceptual priority:

1. urgency/expiration-sensitive actions;
2. actions that unlock dependencies;
3. actions that free constrained capital/resources;
4. short immediate actions;
5. remaining actions by plan utility.

Recompute after every completed step because capital, inventory, urgency and
market evidence may have changed.

### Why this path?

Primary UI stays compact. Progressive detail may explain:

- how many actions/plans were compatible;
- which candidates were excluded by overlapping cash/inventory/exposure;
- slower-turnover or lower-confidence trade-offs;
- urgency effects;
- capital deliberately left free for optionality.

### Hysteresis

Once the owner starts a plan, small score changes should not reshuffle
instructions constantly. A started plan is replaced only when:

- it becomes invalid;
- a hard risk/resource/freshness condition changes materially; or
- another plan is deterministically **materially better** under a versioned
  replacement threshold.

Tiny ranking differences do not replace a plan.

## 13. Step execution freeze

Do not change quantity/price under the user's hands while an instruction is being
entered in GW2.

The current step receives a short execution/freeze window. Background refresh may
update other evidence and may confirm earlier completed steps, but the current
instruction stays stable through immaterial movement.

If new evidence materially invalidates the current instruction before execution,
do not silently rewrite it. Present an explicit French recheck/do-not-execute
state and require confirmation of the updated instruction.

After the step is completed, reconcile the local action, incorporate latest
verified evidence and recompute the next step.

## 14. Verified state plus reversible local execution shadow

ArenaNet/account APIs may lag behind manual human actions. Waiting for every API
refresh would make Active paths unusable.

Canonical concept:

`effective planning state = latest verified ArenaNet state + unconfirmed local execution events`

The shadow is a separate reversible event overlay, **not fake authoritative
inventory**.

Examples of projected effects:

- a completed buy-now step can provisionally reduce cash and add acquired
  material;
- a completed craft step can provisionally consume inputs and add output;
- a completed listing step can provisionally consume available item quantity,
  record expected listing state and account for the applicable listing fee under
  the canonical model;
- a completed buy-order placement can provisionally reserve committed gold.

### `Terminé`

`Terminé` means the user reports that the just-issued instruction was performed
essentially as specified. It does not claim that ArenaNet has already confirmed
it and does not close the economic outcome.

Exceptional secondary controls must allow the user to report a materially
different executed quantity/price **or that the action was not performed** rather
than forcing false exact evidence.

### Undo

Unconfirmed local execution events are reversible. Normal UX supports at least
undoing the latest local step. Earlier-event reversal must also invalidate or
reconcile dependent later local steps rather than leaving an impossible
projected state.

Prefer append-only local event + reversal semantics so effective state can be
reconstructed/debugged deterministically.

## 15. API refresh during an Active path

A refresh may occur at any time.

Rules:

1. never silently mutate the currently executing frozen instruction for an
   immaterial change;
2. reconcile/confirm already completed shadow events whenever evidence supports
   them;
3. after `Terminé`, use the latest verified snapshot plus remaining local events
   to recompute the next instruction;
4. material safety/economic invalidation may interrupt with explicit recheck;
5. refresh does not by itself require the user to wait before continuing the
   path.

Thus an hour of successive Active paths can continue while ArenaNet catches up
in parallel.

## 16. Reconciliation outcomes

A local execution event may become:

- **confirmed** — verified state is consistent; retire the independent shadow
  effect so it is not double-counted;
- **compatible/partial** — verified evidence differs in quantity/detail but is
  reconcilable; update effective state deterministically;
- **not yet observable** — keep the local event provisional only within bounded,
  endpoint-aware propagation/cache expectations;
- **contradicted** — repeated/material verified evidence conflicts with the local
  report; pause the affected plan and show an exceptional French reconciliation
  state instead of guessing.

Verified ArenaNet evidence eventually wins over provisional local assumptions.
The application never asks the user to maintain general accounting manually;
manual confirmation exists only to advance the instruction just issued and to
resolve exceptional ambiguity.

Completed-step UI should distinguish, without excessive noise, between current
instruction, locally recorded/awaiting confirmation, and confirmed-by-ArenaNet
state.

## 17. Execution completion versus economic outcome

A plan can finish its manual execution before its economic outcome is known. For
example, completing a listing instruction means the manual listing action was
reported; it does not mean the item sold.

`plan execution complete != economic outcome complete`

Outcome closes only when later evidence supports the sale/result. This
distinction is required for trustworthy personal performance evaluation.

## 18. Crafting integration

Owned materials are never free. Crafting compares consuming an owned tradable
material against its realistic sale/opportunity value.

For missing ingredients the engine may choose the best supported economic source,
including:

- already owned;
- instant buy;
- buy order;
- craft an intermediate;
- a safe future source explicitly added by policy.

`Mixed procurement` is an internal optimization result, not a user-selected
mode.

If selling raw inputs is economically superior, the craft must not become a
Signal merely because its output price exceeds purchased ingredient cost.

Craft opportunities also require output liquidity/history/confidence. The
crafting workspace displayed as `Artisanat` is for deliberate crafting time,
while qualifying craft plans may also surface in `Mes Signaux`.

## 19. Continuous decision loop

After the plan/crafting foundation, the local application should continuously:

1. refresh evidence when permitted by typed endpoint-aware policy;
2. reconcile verified and local shadow state;
3. update capital/inventory/orders/exposure/resources;
4. invalidate/recheck stale plans;
5. generate current opportunities/plans;
6. resolve conflicts;
7. expose/notify only concrete Signals.

Unchanged Signals are de-duplicated. No-action states stay silent. Operational
health remains visually separate from profit Signals.

## 20. Performance semantics

Main assistant headline: **30-day realized profit**.

Secondary context: 7-day and 90-day realized profit.

Internal realized strategy categories are additive and non-overlapping for the
same supported population/window:

- `Trading/Flipping`;
- `Crafting`;
- `Unclassified`.

The UI renders appropriate French labels for those categories. Ambiguous outcomes
remain `Unclassified`; do not interrupt the owner simply to force attribution.
Later deterministic evidence may reclassify them.

Open/unrealized result is always separate from realized headline performance.

The crafting workspace may additionally show **crafting value added**: economic
value created versus the best realistic alternative for the consumed inputs.
This is analytical context and must not be added again to global realized profit.

## 21. Refresh and external uncertainty

UI freshness reflects the true source/endpoint evidence available. ArenaNet
cache/rate-limit semantics that remain unverified stay in the VERIFY register.
Conservative endpoint-aware policy wins over aggressive polling assumptions.

A local shadow event may bridge UX latency, but it does not change the external
contract or pretend that ArenaNet already confirmed the action.

## 22. 0.1 scope and extensibility

0.1 focuses on Trading Post flipping/trading and crafting.

Existing investment tracking remains preserved but is not a primary navigation
focus and no investment-discovery/seasonality engine is required for 0.1.

Future profit engines may plug into the same
`Opportunity -> Plan -> Steps -> Outcome` model, for example Mystic Forge,
conversions, time-gated crafts, salvage/opening or vendor arbitrage. They must not
create parallel product architectures. Trading Post remains the first focus.

## 23. Non-goals

- automated gameplay or Trading Post mutations;
- runtime LLM/opaque ML ownership of financial truth;
- guaranteed fill/profit/price claims;
- showing analysis simply because it exists;
- forcing the user to operate scanner/history/personal-learning subsystems;
- manually maintained shadow accounting beyond explicit execution confirmation
  and exceptional correction;
- restoring investment discovery to the 0.1 critical path;
- exhaustive unbounded crafting/world optimization.

## 24. Rollout sequence

The owner-approved rollout prioritizes a usable main surface quickly:

1. canonical product/docs transition;
2. self-healing GitHub-authoritative `CURRENT.md` live state;
3. UX design spike;
4. Signals MVP displayed as `Mes Signaux` + new primary navigation;
5. cleanup of superseded UI/docs/code after the MVP proves replacement paths;
6. full plan engine with Active/Passive, reservations, shadow/Undo/reconciliation;
7. crafting economic truth;
8. guided bounded crafting plans;
9. continuous decision loop/local notifications;
10. hardening/packaging;
11. observed plan-outcome evaluation.
