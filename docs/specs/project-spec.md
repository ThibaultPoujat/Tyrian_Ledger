# Project Specification — Tyrian Ledger Personal Profit Assistant

## 1. Product statement

Tyrian Ledger is a local-first personal **second-screen Guild Wars 2 profit
assistant**. It combines the owner's read-only account/Trading Post data, current
public market data, locally accumulated market history and deterministic
financial rules to continuously answer:

- what manual action is worth performing now;
- how much capital/inventory should be committed;
- which candidate actions are mutually compatible;
- what evidence and constraints justify the action;
- what the owner has actually earned;
- whether an executed plan later matched its modeled expectations.

The primary daily Signals surface is displayed as **`Mes Signaux`**. Scanner,
dashboard, raw history, order books, inventory and personal learning are
supporting engines/evidence, not the product's main responsibilities.

The application is not a trading bot, gameplay bot, order executor, browser
automator or autonomous game agent.

## 2. Intended user and philosophy

0.1 is optimized for the owner as a single local user. The product should reduce
market-analysis time enough that the owner can glance at a second monitor,
perform a small number of concrete actions, confirm execution and return to the
game.

Tyrian Ledger favors **repeatable capital turnover, controlled downside,
explainable evidence and low attention cost** over headline ROI. A liquid,
repeatable moderate-return market may be preferable to an extreme spread with
negligible depth or long capital lock.

The application improves through owned evidence rather than opaque prediction.
It records market observations and personal outcomes, then uses those facts with
explicit sample counts, recency and confidence. Personal evidence should
silently improve ranking when sufficiently supported; the user should not need
to operate a separate analytics workflow.

## 3. Core principles

1. **Read-only toward Guild Wars 2.** ArenaNet data may inform actions; only the
   human owner performs Trading Post/game actions.
2. **Local-first privacy.** Account data, history, settings and owned market
   history stay local unless the owner explicitly exports a backup.
3. **Deterministic financial truth.** Authoritative money is integer copper.
   Fees, cost basis, profit, ROI, allocation, opportunity cost, plan selection
   and reconciliation rules are deterministic and tested.
4. **Central fee policy.** One application policy owns separate GW2 listing and
   exchange fee configuration. VERIFY-013 remains open for fractional-copper
   rounding, so fee-derived output remains modeled/provisional under the
   owner-approved configured rounding policy until verified otherwise.
5. **Explainability before cleverness.** Every action/plan exposes evidence and
   binding constraints. No runtime LLM or opaque ML model owns financial truth.
6. **History is evidence, not prophecy.** Observed medians, persistence,
   volatility, liquidity and personal turnover describe evidence; they do not
   guarantee future fills or prices.
7. **Unknown stays unknown.** Missing permissions, incomplete history, unknown
   basis, insufficient samples and contradictory evidence remain explicit.
8. **Hard constraints first.** Failed safety/evidence/liquidity/risk/resource
   requirements cannot be outweighed by a high score.
9. **Capital and inventory are shared resources.** Existing commitments and
   started plans constrain new plans; the same resource cannot be promised twice.
10. **Silence is a feature.** Internal analysis that does not require a concrete
    manual action does not clutter the Signals surface.
11. **The project learns safely.** Observed outcomes may evaluate rules, but
    rules/weights change only through reviewed code/config and never by
    autonomous self-modification.

## 4. Repository language and displayed navigation

Repository-facing artifacts use **English**: documentation filenames and normal
prose, code/type/API/database/migration identifiers, internal domain terms,
reason codes, tests and configuration keys. French appears in repository docs
only when quoting or specifying user-facing product copy.

All user-facing UI/UX labels and text are written in **French**, including
navigation, actions, errors, empty states, notifications, explanations and
accessibility text.

Target displayed primary navigation:

`Mes Signaux / Artisanat / Réglages`

`Mes Signaux` is the displayed label for the default Signals surface.
`Artisanat` is the displayed label for the deliberate crafting-for-profit
workspace. `Réglages` owns account/API, health/refresh, risk/bankroll policy,
notifications, backup/recovery and advanced diagnostics.

Existing investment-position/staged-exit infrastructure is preserved but
investment discovery/seasonal opportunity research is deferred from the 0.1
primary experience.

## 5. Canonical product model

Canonical domain lifecycle:

`Opportunity -> Plan -> Steps -> Reconciliation -> Outcome`

Market/account intelligence is upstream evidence that feeds Opportunity
discovery. A **Signal is not a lifecycle stage**; it is a presentation/eligibility
concept for an opportunity or plan action sufficiently safe, profitable, relevant
and compatible with the owner's current state to justify surfacing a concrete
manual action.

See `docs/specs/signals.md` for the detailed attention, plan, execution,
shadow-state and UX contract.

## 6. Main user journeys

### 6.1 Connect and synchronize

The owner configures a dedicated ArenaNet API key through an OS-backed local
secret mechanism. The local host validates it, exposes only safe permission
status to React and synchronizes the minimum required read-only personal data.

Relevant account scope includes current/completed Trading Post transactions,
wallet Coin, bank/material storage, recipe unlocks and crafting capability where
the verified API/permissions support them.

Partial permission/source failure must degrade only the affected feature and
must not erase previously valid local data.

### 6.2 Observe actual performance

Completed transactions are persisted idempotently. Inventory/cost basis is
reconstructed with deterministic FIFO matching unless a later owner ADR changes
the accounting policy.

The main assistant shows:

- **30-day realized profit** as the headline;
- 7-day and 90-day realized profit as secondary context;
- open/unrealized result separately;
- explicit data coverage and unknown-basis limitations.

Internal realized strategy attribution must be additive/non-overlapping for the
same supported population/window:

- `Trading/Flipping`;
- `Crafting`;
- `Unclassified`.

The UI renders appropriate French labels. Ambiguous outcomes stay `Unclassified`
until later deterministic evidence can resolve them. No screen may claim
lifetime profit for periods the retained data cannot support.

### 6.3 Build and use market intelligence

The broad market scanner, detailed order-book reads and local history collector
remain important internal engines.

Broad screening may use aggregate data; shortlisted candidates use detailed
book/depth evidence when practical. Retained history describes persistence,
stability and liquidity with explicit sample/coverage limitations.

Raw ROI is never sufficient by itself for a high-priority Signal.

The normal user should not need to operate scanner/history pages to benefit from
these engines. Detailed evidence is available through the displayed `Pourquoi ?`
control or advanced diagnostics.

### 6.4 Decide what to do — Signals (`Mes Signaux`)

The Signals surface attention-gates analysis. Internal states such as `WAIT`,
`HOLD`, `KEEP BID`, `LEAVE SELL LISTING`, `SKIP`, `REVIEW` and harmless market
churn normally remain silent.

The user-facing feed focuses on concrete manual actions such as buy now,
place/update/cancel buy order, craft, list/relist, and sell/sell partial.

Initial Signal cards show action, item, quantity/relevant price, modeled result,
confidence and `Pourquoi ?`. Detailed current/history/personal/risk evidence is
progressively disclosed.

If nothing clears the attention gate, the correct product outcome is an
understandable zero-Signal state rather than manufactured work.

Operational health/freshness is shown separately and truthfully by relevant
source. The UI does not invent a uniform refresh age.

### 6.5 Build compatible plans

After the MVP, opportunities are converted into executable plans and compatible
bundles rather than simply sorted by one score.

Hard constraints apply first. Surviving plans are compared using named factors
such as confidence-adjusted economics, capital efficiency/turnover, attention
fit, urgency, concentration/opportunity cost and stability.

Plans reserve shared resources: cash, inventory, current exposure, expected
incoming materials and other relevant commitments. Two plans cannot consume the
same resource simultaneously.

The hard bankroll reserve remains protected. A softer opportunity-capital buffer
may preserve optionality. A preferred attractiveness threshold may relax toward,
but never below, hard evidence/liquidity/risk floors when meaningful capital
would otherwise stay idle. The opportunity reserve is not a fixed deployment
percentage.

Compatible-bundle selection is bounded and deterministic, not unbounded
portfolio optimization or simple top-N sorting.

### 6.6 Choose Passive or Active attention

Passive and Active are execution/attention paths, not permanent account modes.

- **Passive**: typically ~1–3 minutes of interaction, then wait for market fills
  while playing.
- **Active**: immediately executable chains, typically ~5–15 minutes per path,
  with successive paths possible during a longer active session.

A buy-order step may appear in an Active path only as a terminal step; it must
not block later immediate steps.

If only a few obvious actions exist, a single ordered list is preferable to
forcing path choice.

Before selection, Passive/Active proposals may be alternatives that overlap
resources. Once the owner selects displayed `Démarrer`, the chosen plan reserves
its resources and alternatives are recomputed from what remains. A waiting
Passive plan may coexist with Active work that uses only unreserved resources.

### 6.7 Execute without waiting for API propagation

The current manual instruction receives a short execution/freeze window so
quantity/price does not silently change while the owner is entering it in game.
Material invalidation may force an explicit recheck; immaterial market movement
does not reshuffle the instruction.

Effective planning state is:

`latest verified ArenaNet state + locally recorded unconfirmed execution events`

The local execution shadow is reversible/provisional and separate from verified
state. Displayed `Terminé` reports that the issued instruction was performed
essentially as specified, allowing the next step to be planned immediately.
Exceptional controls allow different quantity/price or reporting that the action
was not performed.

Undo must at least support reversing the latest unconfirmed local step. Earlier
reversal must safely invalidate/reconcile dependent later local steps.

API refresh may confirm earlier completed steps while an Active path continues.
Verified evidence eventually wins. Material contradiction pauses the affected
plan for explicit reconciliation rather than guessing.

Executing a listing step does not close the economic outcome; sale/result closes
only when later evidence supports it.

### 6.8 Craft for profit

Crafting treats owned tradable materials as economic assets, never free inputs.
It compares consuming them against their realistic sale/opportunity value.

Direct ingredient acquisition may include owned quantity, instant buy and
bounded buy-order procurement. Bounded recipe search may additionally choose to
craft intermediates. `Mixed procurement` is internal optimization, not a user
mode.

Crafting must compare against selling raw inputs. If that alternative is
superior, the craft is not an actionable profit opportunity.

Output liquidity/history/confidence is required before a theoretical margin can
become a Signal.

The crafting workspace, displayed as `Artisanat`, presents a small number of
guided profitable plans rather than an exhaustive world spreadsheet. Qualified
craft plans may also appear in `Mes Signaux`.

The crafting workspace may additionally report **crafting value added** versus
the best realistic input alternative. This analytical measure must not be added
again to global realized profit.

### 6.9 Continuous decision loop

A later local loop should, when permitted by verified endpoint/rate/cache policy:

1. refresh relevant evidence;
2. reconcile verified and local execution state;
3. update capital/inventory/orders/exposure/resources;
4. invalidate/recheck stale plans;
5. regenerate opportunities/plans;
6. resolve resource conflicts and attention gates;
7. surface/notify only materially new or changed concrete Signals.

Unchanged Signals are de-duplicated. No-action states stay silent. Operational
health is not a profit Signal.

### 6.10 Learn from observed outcomes

Tyrian Ledger preserves enough versioned context to compare modeled plan results
with actual outcomes where user execution can be meaningfully reconciled.

Observed metrics may include realized result, holding/capital-lock duration,
turnover, low-liquidity failures, plan invalidation/replacement and calibration
by confidence/utility buckets where sample size is sufficient.

Do not fabricate counterfactual profit for ignored/unexecuted Signals. Do not
self-modify rules from outcome reports.

## 7. Risk and bankroll behavior

Risk policy is configurable and visible. Existing reference defaults include a
meaningful cash reserve (currently approximately 15%), liquidity-sensitive
single-item caps, existing order/position exposure, visible-depth participation
limits and strategy/category concentration caps.

These are centralized deterministic policies, not constants to scatter through
React or features.

Started plans and passive commitments count against resources available to new
plans. Hard safety/evidence/liquidity/risk floors never relax because the system
has few candidates. A softer attractiveness threshold may relax toward the hard
floor when meaningful deployable capital would otherwise remain idle, but it
must still clear an absolute attention/value threshold.

## 8. Fee and relisting semantics

The externally documented listing fee is non-refundable. A completed Trading
Post sale pays listing and exchange fees under the canonical fee policy.

Final realized P&L must retain all applicable listing fees paid across repeated
relist attempts plus the final exchange fee where the retained evidence supports
those actions.

For the marginal decision `should I relist now?`, already-paid listing fees are
sunk. Compare leaving the current listing against paying the **next** listing fee
and the modeled benefit/risk of the new outcome. Do not recommend cosmetic
one-copper churn.

Fractional-copper rounding remains provisional while VERIFY-013 is open.

## 9. Data ownership and persistence

SQLite is the durable local store. Completed personal transactions and retained
market history must remain recoverable according to their owning tickets.

Sync is idempotent and partial remote failures must not erase previously valid
local state. Backups are explicit local artifacts controlled by the owner; no
automatic cloud upload exists in 0.1.

Derived FIFO/statistical/recommendation/plan state must be reproducible or
versioned from authoritative inputs. Local execution shadow events are stored
separately from verified account state and reconciled rather than silently
merging authority.

## 10. Non-functional requirements

- Local host binds to loopback by default.
- Host-header validation permits only explicit local host values.
- Production frontend/API are same-origin; development CORS allowlists exact
  configured trusted origins; wildcard origins are forbidden.
- State-changing local endpoints have explicit cross-origin/anti-forgery
  protection independent of CORS.
- Browser never receives credentials/secrets.
- Current desktop browsers remain the target; automated browser coverage uses
  supported Playwright engines.
- UI supports keyboard navigation, semantic controls, sensible focus and WCAG
  2.2 AA contrast.
- Clean-checkout build/test/start instructions are maintained.
- Database migrations/integrity/recovery are versioned and tested.
- Secret scanning remains part of project hygiene.
- Normal tests use fixtures/mocks instead of live ArenaNet calls.
- Material external API uncertainty belongs in VERIFY.
- No decorative dependence on proprietary Guild Wars 2 UI assets.

## 11. Explicit non-goals

- automated Trading Post order placement/cancellation/update;
- gameplay automation;
- autonomous capital deployment;
- runtime LLM ownership of financial decisions;
- opaque ML price prediction/ranking;
- guaranteed fill/profit/price claims;
- cloud multi-user hosting in 0.1;
- exhaustive high-frequency scraping of every order book;
- treating unknown basis or owned materials as free;
- forcing the user to operate raw scanner/history/personal-learning subsystems;
- investment discovery/seasonality on the 0.1 critical path;
- exhaustive unbounded crafting optimization.

## 12. Current success checkpoints

The existing foundation through #92 already provides accounting, live/current
market analysis, retained history, recommendation/scoring/sizing, personal
turnover/ranking and crafting-account ingestion.

Next checkpoints:

- after #131, the owner can use a first attention-first French Signals UI
  displayed as `Mes Signaux` with the new primary navigation;
- after #133, Signals become stable Active/Passive execution plans with shared
  resource reservations and responsive shadow/reconciliation;
- after #93/#94, crafting becomes a first-class economic/guided profit engine;
- after #95, new state can surface concrete Signals without repeated manual
  checking;
- after #96/#97, the 0.1 product is hardened and can evaluate observed plan
  outcomes without fabricated counterfactuals.

## 13. Completion standard for authoritative work

A financial/recommendation/state ticket is not complete because the UI looks
right. It requires deterministic tests, edge/boundary cases, acceptance-criteria
coverage, the review path required by `docs/workflow/model-effort-guide.md`, and
a plain-language functional summary the owner can verify.
