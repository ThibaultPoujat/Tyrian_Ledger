# Tyrian Ledger Visual and Interaction Reference

Status: **Owner-approved visual/interaction direction**

Reviewed: 2026-09-22

Live prototype:

- https://tyrian-ledger-concept.thibault-poujat.chatgpt.site/

## Purpose

This document records the owner-approved visual and interaction direction that
followed the first `Mes Signaux` MVP.

The live ChatGPT Sites prototype is intentionally useful as an interactive
reference: Codex may navigate its pages, change prototype scenarios, inspect
state changes and capture screenshots while implementing related frontend work.

It is **not** an executable product specification and it is not a source of
financial or domain truth.

## Authority order

When the prototype and repository disagree, use this order:

1. typed backend/domain contracts and tested financial/accounting behavior;
2. canonical product/spec/architecture documents, especially
   `docs/specs/signals.md`, `docs/specs/trading-rules.md` and relevant
   architecture/VERIFY records;
3. `docs/ux/ux.md`;
4. this visual-reference decision record;
5. the live prototype and repository screenshots.

The prototype must never override Signal eligibility, financial calculations,
freshness policy, confidence semantics, resource constraints, plan lifecycle,
reconciliation, ArenaNet evidence, or typed backend state.

Business logic must never depend on displayed/localized strings.

## Approved visual and interaction direction

### Future primary navigation

After #133 implements the shared plan engine, the planned navigation direction
may become:

`Mes Signaux / Plans / Artisanat / Réglages`

- `Mes Signaux` remains the default/home attention surface.
- `Plans` becomes first class only when #133 implements the shared started-plan
  execution model.
- `Artisanat` is a guided profit/action workspace, not a recipe browser.
- `Réglages` owns account/data/recovery/diagnostic controls.

Evidence/history is contextual and should normally be reached from a Signal or
Plan through `Pourquoi ?` / relevant-history disclosure rather than becoming a
primary destination.

### Signals hierarchy

The approved layout is not a wall of equal cards.

- A truly corrective/time-sensitive action may receive expanded
  `Prioritaire` treatment.
- Secondary eligible actions remain compact.
- Quantity and actionable per-unit price/maximum price are easier to find than
  modeled result.
- Item imagery is retained when a verified backend URL is available, with a
  stable non-blocking fallback.
- The surface may explain why so few Signals are visible: waiting, holding and
  ordinary fluctuations are not tasks.

The zero state remains a successful state:

`Aucun signal ne mérite votre attention pour le moment.`

### Distinctive action families

Action families should be recognizable very quickly without becoming unrelated
mini-apps.

Use a coherent combination of:

- explicit action text;
- iconography;
- internal card arrangement;
- emphasized metrics;
- subtle accent treatment;
- item image.

Do **not** rely on color alone.

Current displayed action vocabulary remains:

- `ACHETER MAINTENANT`;
- `PLACER UN ORDRE D'ACHAT`;
- `METTRE À JOUR L'ORDRE D'ACHAT`;
- `ANNULER L'ORDRE D'ACHAT`;
- `FABRIQUER`;
- `METTRE EN VENTE`;
- `REMETTRE EN VENTE`;
- `VENDRE MAINTENANT`;
- `VENDRE PARTIELLEMENT`.

The action-specific layout should follow the manual GW2 gesture where useful,
for example quantity + ceiling for a bid, sell/keep quantity for a partial sale,
or before/after steps for a correction.

### Performance

Keep realized performance visually secondary to actionable work and stable
across screen states.

Preferred desktop direction is the compact left sidebar:

- `Aujourd'hui` visible;
- `30 jours` visible;
- `7 jours` and `90 jours` behind disclosure;
- unrealized/open result remains separate.

### Confidence and evidence

Use qualitative confidence only:

- `élevée`;
- `moyenne`;
- `limitée`.

Confidence is evidence quality, **not a forecast**.

`Pourquoi ?` may decompose it into distinct evidence dimensions such as:

- historical coverage;
- market behavior/stability;
- current market freshness;
- account freshness/personal evidence;
- important missing evidence.

A contextual history view may use one restrained chart when it genuinely helps,
plus coverage, behavior, freshness, missing evidence and a short plain-language
interpretation. Do not turn Tyrian Ledger into a chart-heavy trading terminal.

### Source freshness and coverage

Market, ArenaNet account and retained market history are distinct sources.

Compact presentation may use truthful values such as:

- `Marché · 42 s`;
- `Compte ArenaNet · 2 min`;
- `30 j complets · 90 j partiels`.

History coverage and last-observation freshness are distinct concepts and must
not be conflated.

Degraded data must gate only the actions that actually require the stale/missing
source. A stale ArenaNet account snapshot must not leave an open-order,
inventory, balance or position-dependent Signal eligible merely because the
prototype happened to render it.

### Corrective actions and Plans

A compound manual correction is not one fake atomic button.

Example:

1. cancel the existing order;
2. place the replacement order.

The Signal may open a short corrective Plan.

The approved Plan presentation emphasizes one useful action at a time and may
display the human lifecycle approximately as:

`Action requise -> déclaré fait -> attente ArenaNet -> confirmé -> écart détecté`

These are presentation concepts over typed backend state. The frontend must not
derive lifecycle logic from the French labels.

`Passive` means the application currently has no reason to demand attention;
it does not mean the Plan has been forgotten or completed.

ArenaNet contradictions enter explicit calm reconciliation rather than silently
trusting either the local report or a stale snapshot.

### Crafting

`Artisanat` uses the same attention/action model as trading.

It is not a table of every recipe. Show only profitable/feasible crafting work
that deserves attention, potentially as:

- craft -> list;
- acquire -> craft;
- later bounded multi-step Plans.

Financial and procurement truth remain backend-authoritative.

### Settings and public-history recovery

`Réglages` should preserve a visible conceptual boundary between:

- personal ArenaNet/account synchronization and local account state;
- public market-history cache/coverage/recovery;
- diagnostics and local-data safety/recovery.

Rebuilding public market history must not imply sending or deleting personal
account data.

## Prototype details that are not authoritative

Do not copy these blindly:

- mock counts of Signals, Plans, watched situations or cached items;
- mock prices, profits, quantities, durations or performance;
- prototype-only confidence/coverage labels not backed by real contracts;
- degraded-state cards that contradict real source dependencies;
- any scheduler/countdown not implemented by the real application;
- exact plan transitions unless supported by typed domain state;
- any financial formula or eligibility rule inferred from the screenshots;
- exact French wording when a canonical copy contract already exists elsewhere.

Pixel-perfect cloning is not required. Preserve the information hierarchy,
interaction model, desktop density and action-family recognizability while
using the real application's components, accessibility rules and backend
contracts.

## Visual artifact availability

Codex is expected to inspect the live prototype directly when browser access is
available. The owner-approved repository documents remain sufficient to continue
implementation if the external prototype is temporarily unavailable.

When reviewing a UI change, capture fresh screenshots from the real application
or the live prototype as review evidence rather than treating old prototype
screenshots as executable specification.

## Codex implementation instruction

For #133/#142 and later frontend work:

1. inspect this document, `docs/ux/ux.md`, relevant specs/contracts and the live
   prototype before implementation;
2. use the prototype as the approved visual/interaction direction;
3. use repository contracts and typed backend state as semantic authority;
4. do not implement financial/domain logic from prototype text or mock values;
5. do not reproduce known prototype inconsistencies;
6. prefer matching hierarchy, density, gesture recognizability and interaction
   flow over pixel-perfect copying;
7. preserve French user-facing copy, accessibility, responsive safety and
   backend-authoritative money/action semantics.
