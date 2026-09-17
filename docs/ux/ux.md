# UI/UX Specification

## Product interaction goal

Tyrian Ledger is a **second-screen profit assistant**. The primary experience
must reduce analysis time and surface only the concrete manual actions that are
worth the owner's attention now.

The default daily surface is **`Mes Signaux`**. Scanner, raw history, inventory,
personal learning and detailed order-book analysis are supporting engines and
evidence, not competing home pages.

## Language

All user-facing labels and text are written in **French**. This includes
navigation, actions, buttons, headings, helper text, errors, empty states,
degraded states, notifications, explanations and accessibility text.

Internal code/API/type names may remain English. Proper nouns and technical
identifiers may remain canonical when translating them would reduce clarity.

## Primary navigation

Target primary navigation:

`Mes Signaux / Artisanat / Réglages`

- `Mes Signaux` is the default/home destination.
- `Artisanat` is the deliberate guided crafting-for-profit workspace. Until it
  is implemented it may be visible but disabled/clearly marked unavailable;
  do not create a fake empty workspace.
- `Réglages` groups API/account connection, sync/refresh/health, bankroll/risk
  settings, alerts, collection/history settings, backup/restore/data controls
  and advanced diagnostics.

Dashboard, Scanner, Investments, raw Inventory and Personal Learning are not
primary navigation destinations. Temporary diagnostic/support routes may remain
until the dedicated post-MVP cleanup ticket verifies that their useful
capabilities have been absorbed elsewhere.

## Visual direction

Modern analytical interface with restrained Guild Wars 2-inspired atmosphere
without copying ArenaNet assets.

Use:

- dark/charcoal base;
- subtle metallic/parchment surfaces;
- restrained accents;
- clear gold/copper emphasis for economic values;
- strong grouping and hierarchy;
- high but controlled information density suitable for a second monitor;
- simple icons/CSS shapes rather than copied game assets.

The visual design should communicate a private intelligence service more than a
spreadsheet. Function comes before lore decoration.

## Mes Signaux

### Attention gate

If an item appears in the normal `Mes Signaux` action feed, it is because the
user should act.

Internal no-action states such as `WAIT`, `HOLD`, `KEEP BID`, `SKIP`, `REVIEW`,
`LEAVE SELL LISTING`, harmless undercut/outbid and similar observations normally
stay silent.

Operational problems are separate from profit Signals. A small status area may
show stale data, missing permission, sync failure or ArenaNet unavailability
without presenting those as economic actions.

### Signal card

Default Signal cards are deliberately compact. First view should show:

- concrete French action;
- item;
- quantity;
- relevant price or maximum price where applicable;
- modeled result/profit;
- confidence;
- `Pourquoi ?` progressive disclosure.

Detailed ROI, depth, spread, history, personal evidence, anomaly flags and risk
constraints live behind `Pourquoi ?` unless one is itself the reason the action
must change immediately.

Example hierarchy:

```text
PLACER UN ORDRE D'ACHAT
22 × Objet X @ <= 41s 27c
Profit modélisé : +2g 18s
Confiance élevée
[Pourquoi ?]
```

Exact final French copy is validated in the UX spike; financial/action semantics
remain structured backend output.

### Zero-Signal state

Do not manufacture work to avoid an empty screen. A suitable state is equivalent
to:

`Aucun signal ne mérite votre attention pour le moment.`

The screen may still show compact operational health/freshness and realized
performance.

### Many candidates

The user should never receive a wall of raw candidates. The backend/planner may
analyse hundreds of possibilities; the screen shows only the attention-worthy
subset. When full plan orchestration is available, show the best compatible
Passive/Active paths rather than an arbitrary top-N list.

## Performance placement

On `Mes Signaux`, show **30-day realized profit** as the headline performance
number. 7-day and 90-day realized results are secondary context.

Open/unrealized result is visually and semantically separate and must not be
added to realized profit.

Strategy attribution later uses additive non-overlapping categories:

- Trading/Flipping;
- Crafting;
- Non classé / Unclassified.

Do not force attribution when evidence is ambiguous.

## Freshness and status

Freshness must reflect the real relevant data source. Do not collapse different
ArenaNet/public/history refresh semantics into a fabricated single age.

Where useful, show source-specific age such as market/account/history evidence.
Use understandable French degraded states and preserve the difference between:

- loading;
- account not connected;
- permission unavailable;
- sync not yet performed;
- stale retained data;
- upstream unavailable;
- local error.

## Passive and Active paths

Passive/Active paths arrive after the initial MVP and represent attention/time,
not persistent account modes.

### Passive

Designed for approximately 1–3 minutes of interaction, such as place/update/
cancel orders or listings that can then wait for market fills while the user
returns to gameplay.

### Active

Designed for immediately executable chains, usually approximately 5–15 minutes
per path, such as buy-now -> craft -> list. The user may complete successive
Active paths for much longer overall.

If only a few obvious actions exist, prefer one simple ordered list rather than
forcing path choice.

Before selection Passive/Active proposals may be alternatives. After
`Démarrer`, the chosen plan reserves resources and the other proposal is
recomputed from what remains.

Path summary should keep plan-level values scannable:

- modeled profit;
- capital committed;
- approximate interaction time;
- confidence;
- optionality/opportunity capital intentionally left available where relevant.

## Started-plan execution

Completed steps remain visible/collapsed until the path finishes so the user can
trust what state the assistant believes.

The current instruction must not mutate under the user's hands. During a short
execution window, background refresh may confirm prior steps but does not change
current quantity/price for immaterial movement. Material invalidation uses an
explicit French recheck state rather than silent rewriting.

A normal step offers one simple `Terminé` confirmation. Secondary exceptional
controls may allow recording a different quantity/price or saying the action was
not performed.

Unconfirmed local steps should show a subtle state equivalent to `En attente de
confirmation ArenaNet` rather than pretending verification has occurred.

At minimum, provide `Annuler la dernière étape` for an unconfirmed local event.
If undoing an earlier event would invalidate later locally recorded steps, say so
explicitly and reconcile them together rather than leaving impossible state.

If later verified evidence materially contradicts local execution, pause the
affected path with an exceptional reconciliation state rather than guessing.

## Artisanat

`Artisanat` is a guided active-profit workspace, not a giant recipe spreadsheet.

Present a small number of profitable, feasible plans with:

- modeled profit;
- capital required;
- confidence/liquidity/history evidence;
- approximate interaction time;
- key sourcing strategy;
- progressive recipe/input detail.

The engine chooses economic procurement; the user does not select a separate
`mixed procurement mode`.

Owned materials remain visibly owned but use opportunity cost rather than zero
cost. If selling the raw materials is economically superior, the craft should
not be shown as profitable/actionable.

Craft plans use the same shared execution/step/reconciliation model as trading.

## Réglages

Organize configuration around user goals rather than backend subsystems:

- API/account connection and safe permission state;
- sync/refresh and data-source health;
- bankroll reserve/risk/concentration policy;
- alert/notification controls;
- history/collection status and advanced diagnostics;
- backup/restore/clear-local-data with explicit destructive confirmation.

The API key value never appears in normal UI after storage.

## Supporting evidence and personal learning

Personal learning should improve ranking silently when evidence is sufficient.
The user should not need to operate a large Personal Learning table.

When personal evidence materially affects a Signal, expose it inside
`Pourquoi ?` with sample size/recency/limitations.

Raw scanner/history/order-book details may remain available through progressive
detail or diagnostics but should not compete with the main assistant.

## Interaction rules

- Do not hide critical assumptions only in tooltips.
- Use qualifiers equivalent to `Profit modélisé`, `Médiane observée` or other
  truthful certainty labels where relevant.
- Never show guaranteed profit/fill/price language.
- Unknown/insufficient evidence is a first-class state, not zero/blank.
- Keep filters/selections stable where practical.
- Expose binding risk/resource constraints through `Pourquoi ?`.
- Destructive backup/restore/clear actions require understandable confirmation
  and error recovery.
- Support keyboard navigation, semantic controls, sensible focus and WCAG 2.2
  AA contrast.

## Desktop-first / second-screen-first

Primary optimization is desktop and second-monitor use. The user should be able
to glance, execute, confirm and return to the game quickly.

Responsive behavior should prevent unusable overflow at narrower desktop/tablet
widths. Full phone-first optimization may be deferred unless a later ticket
prioritizes it.

## UX validation strategy

Before implementing the first `Mes Signaux` MVP, validate at least these states
with a lightweight prototype/wireframe:

1. zero Signals;
2. two simple Signals;
3. many underlying candidates but few displayed Signals;
4. market fresh/account stale and the inverse;
5. degraded/sync failure;
6. urgent corrective action;
7. expanded `Pourquoi ?`;
8. compact performance summary;
9. target primary navigation.

ChatGPT Sites may be used when available in the owner's region, but tool
availability must never block the design spike or MVP.
