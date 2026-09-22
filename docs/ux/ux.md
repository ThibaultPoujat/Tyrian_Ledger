# UI/UX Specification

## Product interaction goal

Tyrian Ledger is a **second-screen profit assistant**. The primary experience
must reduce analysis time and surface only the concrete manual actions that are
worth the owner's attention now.

The default daily Signals surface is displayed as **`Mes Signaux`**. Scanner,
raw history, inventory, personal learning and detailed order-book analysis are
supporting engines and evidence, not competing home pages.

## Language boundary

Repository-facing names and internal UI/domain semantics are English. Exact
French strings in this document represent displayed product copy.

All user-facing labels and text are written in **French**. This includes
navigation, actions, buttons, headings, helper text, errors, empty states,
degraded states, notifications, explanations and accessibility text.

Internal code/API/type names, route/domain identifiers and reason codes remain
English. Proper nouns and technical identifiers may remain canonical when
translation would reduce clarity.

## Primary navigation

Current implemented navigation before #133:

`Mes Signaux / Artisanat / Réglages`

- `Mes Signaux` is the default/home Signals destination.
- `Artisanat` is the displayed label for the guided crafting-for-profit
  workspace. Until implemented it may be visible but disabled/clearly marked
  unavailable; do not create a fake empty workspace.
- `Réglages` is the displayed label for the settings destination. For the MVP it
  must be functional, grouping/reusing existing API/account, sync/health, risk,
  data/backup and diagnostic controls rather than routing to a dead placeholder.

Dashboard, Scanner, Investments, raw Inventory and Personal Learning are not
primary navigation destinations. Temporary diagnostic/support routes may remain
until the dedicated post-MVP cleanup ticket verifies that their useful
capabilities have been absorbed elsewhere.

After #133 implements the shared typed plan engine, the approved target becomes:

`Mes Signaux / Plans / Artisanat / Réglages`

`Plans` is a first-class destination for started/manual execution paths that
still require lifecycle or reconciliation work. It must not be exposed as a fake
functional destination before that typed execution model exists.

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

For the first Signals MVP, the owner-reviewed decision record is
`docs/ux/signals-second-screen-spike.md`. When this general UX specification and
that record differ on the first `Mes Signaux` implementation, the spike's
explicit owner-reviewed decisions govern #131.

For the post-MVP visual and interaction direction, use
`docs/ux/tyrian-ledger-visual-reference.md` and the live prototype when it is
available. The published ChatGPT Sites prototype is a visual/interaction reference only:
repository UX/spec/domain contracts remain authoritative for Signal eligibility,
financial calculations, freshness, confidence semantics, plan lifecycle and
reconciliation.

## Signals surface (`Mes Signaux`)

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

Signal presentation is intentionally hierarchical rather than uniform:

- one corrective/time-sensitive action may receive dominant `Prioritaire`
  treatment when it truly deserves first attention;
- other eligible actions stay compact and scannable;
- different action families should be recognizable at a glance through a
  coherent combination of iconography, internal layout, emphasized metrics and
  action text; color may reinforce the distinction but must never be the only
  cue.

Default Signal cards are deliberately compact, but **execution information has
the strongest visual priority**. First view should show:

- concrete French action;
- item image when available, with a stable fallback glyph;
- item;
- quantity;
- relevant per-unit price or maximum price;
- modeled result/profit as secondary evidence;
- qualitative confidence;
- `Pourquoi ?` progressive disclosure.

For Trading Post actions, mirror the in-game entry grammar rather than showing an
abstract multiplication expression. Quantity appears first, followed by the
per-unit price split into gold/silver/copper denomination groups. Each numeric
denomination is immediately followed by its coin glyph. Accessible text still
exposes the full monetary value semantically.

Example hierarchy:

```text
PLACER UN ORDRE D'ACHAT                                      Confiance élevée
Objet X

Quantité à saisir
22

Prix max. par unité
0 [gold] 41 [silver] 27 [copper]

Profit modélisé : +2 [gold] 18 [silver]                      [Pourquoi ?]
```

The quantity and actionable price must be easier to find than modeled profit,
because they are the values the owner physically reproduces in Guild Wars 2.

Detailed ROI, depth, spread, history, personal evidence, anomaly flags and risk
constraints live behind `Pourquoi ?` unless one is itself the reason the action
must change immediately.

The normal surface may explain its deliberate quietness with compact copy
equivalent to `Pourquoi si peu de signaux ?`: waiting, holding and ordinary
fluctuations are not work. Such explanation must not turn hidden candidates into
a monitoring dashboard, and any displayed counts must reflect real eligible or
tracked populations rather than decorative prototype numbers.

Displayed action copy validated by the spike includes
`ACHETER MAINTENANT`, `PLACER UN ORDRE D'ACHAT`,
`METTRE À JOUR L'ORDRE D'ACHAT`, `ANNULER L'ORDRE D'ACHAT`,
`FABRIQUER`, `METTRE EN VENTE`, `REMETTRE EN VENTE`,
`VENDRE MAINTENANT` and `VENDRE PARTIELLEMENT`. Financial/action semantics
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

On desktop, prefer the compact left sidebar as the stable home for realized
performance so it remains visible without competing with the next action. Keep
the realized-performance block anchored in the same screen position across
normal, empty, degraded and corrective states.

Show **today's realized profit** and **30-day realized profit** by default.
7-day and 90-day realized results remain available behind a compact disclosure
rather than occupying the permanent header.

Open/unrealized result is visually and semantically separate and must not be
added to realized profit.

Internal strategy categories remain English and non-overlapping:

- `Trading/Flipping`;
- `Crafting`;
- `Unclassified`.

Displayed labels are French; exact wording is validated during UX work (for
example an appropriate French trading/flipping label, `Artisanat`, and
`Non classé`). Do not force attribution when evidence is ambiguous.

## Freshness and status

Freshness must reflect the real relevant data source. Do not collapse different
ArenaNet/public/history refresh semantics into a fabricated single age.

Use explicit source meanings rather than terse ambiguous labels:

- `Marché actualisé il y a …`;
- `Compte ArenaNet synchronisé il y a …`;
- `Historique marché — dernier échantillon enregistré il y a …`.

Compact status pills may shorten those labels when the meaning remains explicit,
for example `Marché · 42 s`, `Compte ArenaNet · 2 min`, or historical coverage
such as `30 j complets · 90 j partiels`. History coverage and history recency
are not interchangeable; expose the one that actually supports the decision.

The compact default may show current market freshness plus a `Données`
disclosure for account/history detail. Show a next-refresh countdown only when
a scheduler genuinely exists and knows the next planned refresh time. Use
`Actualisation en cours…` while refreshing. Before #95 adds the continuous
Signal loop, #131 truthfully displays `Actualisation à la demande`; use
`Actualisation automatique` only once an automatic scheduler actually exists.
Do not invent a progress bar/countdown merely to imply activity.

Use understandable French degraded states and preserve the difference between:

- loading;
- account not connected;
- permission unavailable;
- sync not yet performed;
- stale retained data;
- upstream unavailable;
- local error.

The current Signals MVP treats stale ArenaNet account evidence as a separate
operational state and suppresses actions conservatively. A future typed
action-source dependency model may refine that gate by withholding only Signals
that require stale balances, inventory, positions or open orders while retaining
a genuinely independent market/history Signal. It must not keep an
account-dependent action visible merely because another source is healthy, and
it must not be inferred only from displayed UI strings.

## Passive and Active paths

Passive/Active paths arrive after the initial MVP and represent attention/time,
not persistent account modes.

### Passive

Designed for approximately 1–3 minutes of interaction, such as place/update/
cancel orders or listings that can then wait for market fills while the user
returns to gameplay. A waiting Passive plan may remain in progress while Active
work uses only unreserved resources.

### Active

Designed for immediately executable chains, usually approximately 5–15 minutes
per path, such as buy-now -> craft -> list. The user may complete successive
Active paths for much longer overall.

A buy-order step may appear in an Active path only as its terminal step; it must
not block later immediate steps.

If only a few obvious actions exist, prefer one simple ordered list rather than
forcing path choice.

Before selection Passive/Active proposals may be alternatives and may overlap
resources. After displayed `Démarrer`, the chosen plan reserves resources and
all alternatives are recomputed from what remains.

Path summary should keep plan-level values scannable:

- modeled profit;
- capital committed;
- approximate interaction time;
- confidence;
- optionality/opportunity capital intentionally left available where relevant.

A progressive `Pourquoi ce parcours ?`/equivalent explanation may disclose
compatible-plan count, capital/inventory conflicts, slower-turnover exclusions,
urgency and capital deliberately left free.

A future optional displayed time-budget control such as `2 min / 10 min / 20+
min` may shape Active-plan construction. It must remain a planning input, not a
persistent user mode.

## Started-plan execution

Completed steps remain visible/collapsed until the path finishes so the user can
trust what state the assistant believes.

The current instruction must not mutate under the user's hands. During a short
execution window, background refresh may confirm prior steps but does not change
current quantity/price for immaterial movement. Material invalidation uses an
explicit French recheck/do-not-execute state rather than silent rewriting.

A normal step offers one simple `Terminé` confirmation. Secondary exceptional
controls allow recording a different quantity/price or saying the action was not
performed.

Completed-step presentation should map the human interaction to explicit typed
state, for example:

- action required;
- locally reported as done;
- awaiting ArenaNet confirmation;
- confirmed by ArenaNet;
- discrepancy detected / reconciliation required.

Displayed French may be concise, but business logic must never infer lifecycle
state from those strings.

At minimum, provide `Annuler la dernière étape` for an unconfirmed local event.
If undoing an earlier event would invalidate later locally recorded steps, say so
explicitly and reconcile them together rather than leaving impossible state.

If later verified evidence materially contradicts local execution, pause the
affected path with an exceptional reconciliation state rather than guessing.

## Crafting workspace (`Artisanat`)

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

## Settings (`Réglages`)

Organize configuration around user goals rather than backend subsystems:

- API/account connection and safe permission state;
- sync/refresh and data-source health;
- bankroll reserve/risk/concentration policy;
- alert/notification controls;
- public market-history cache/coverage/recovery, kept conceptually separate from
  personal ArenaNet/account data;
- advanced diagnostics;
- backup/restore/clear-local-data with explicit destructive confirmation.

The API key value never appears in normal UI after storage.

## Contextual evidence and personal learning

Evidence/history is contextual support for a decision, not a competing primary
navigation destination. A Signal or Plan may open `Pourquoi ?` and then a
relevant-history view that contains only useful evidence for that decision, such
as restrained charting, coverage, behavior/stability, source freshness and
important missing evidence. Avoid turning Tyrian Ledger into a chart-heavy
trading terminal.

Personal learning should improve ranking silently when evidence is sufficient.
The user should not need to operate a large Personal Learning table.

When personal evidence materially affects a Signal, expose it inside
`Pourquoi ?` with sample size/recency/limitations.

Raw scanner/history/order-book details may remain available through progressive
detail or diagnostics but should not compete with the main assistant.

## Interaction rules

- Do not hide critical assumptions only in tooltips.
- Use French qualifiers equivalent to `Profit modélisé`, `Médiane observée` or
  other truthful certainty labels where relevant.
- Never show guaranteed profit/fill/price language.
- Unknown/insufficient evidence is a first-class state, not zero/blank.
- Keep filters/selections stable where practical.
- Expose binding risk/resource constraints through progressive explanation.
- Destructive backup/restore/clear actions require understandable confirmation
  and error recovery.
- Support keyboard navigation, semantic controls, sensible focus and WCAG 2.2
  AA contrast.

## Desktop-first / second-screen-first

Primary optimization is desktop and second-monitor use. The first Signals MVP is
validated explicitly at **1920×1080**. The user should be able to glance,
execute, confirm and return to the game quickly.

Keep the compact left navigation and realized-performance block in stable
positions across screen states. Use action-type color as a fast secondary cue,
but never rely on color alone: the explicit action text remains authoritative.
Avoid decorative rounded footers; prefer no footer unless a quiet flat
functional status region is genuinely needed.

Responsive behavior should prevent unusable overflow at narrower desktop/tablet
widths. Full phone-first optimization may be deferred unless a later ticket
prioritizes it.

## UX validation strategy

Before implementing the first Signals MVP displayed as `Mes Signaux`, validate
at least these states with a lightweight prototype/wireframe:

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
