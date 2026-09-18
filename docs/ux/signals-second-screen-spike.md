# Signals Second-Screen UX Spike

GitHub issue: #130

Status: **Owner-reviewed and ready for implementation handoff**

This document is the lightweight prototype and durable UX decision record for the
first Signals experience displayed as `Mes Signaux`. It is intentionally
implementation-agnostic. Exact French strings below are proposed displayed copy;
repository prose and internal terminology remain English.

## 1. Scope

This spike validates the first usable second-screen Signals MVP before #131
implements it.

It covers:

- the primary shell and navigation;
- Signal-card hierarchy;
- zero-Signal, normal, crowded-candidate, stale/degraded and corrective-action states;
- source-specific freshness;
- compact realized-performance placement;
- `Pourquoi ?` progressive disclosure;
- French displayed copy;
- desktop/second-screen density and accessibility.

It does **not** design the later Active/Passive execution flow, `Démarrer`,
`Terminé`, local shadow state, Undo, reconciliation interactions, or crafting
workspace behavior beyond the disabled navigation destination.

## 2. UX principles

1. **Action first.** If a normal Signal card is visible, the owner has a concrete
   manual action worth taking.
2. **Silence is useful.** Do not expose `WAIT`, `HOLD`, `KEEP BID`,
   `REVIEW`, harmless undercut/outbid, or other no-action analysis.
3. **Execution first.** The easiest information to find on every Signal is the
   exact manual action the owner must reproduce in game: action, item, quantity
   and actionable price/maximum price. These execution values have stronger
   visual priority than modeled profit.
4. **Compact before complete.** The first view answers what to do, with what,
   how much, at what price, and why it is worth attention.
5. **Truthful uncertainty.** Modeled results are labeled as modeled; confidence
   is qualitative rather than false numeric precision.
6. **Operational truth is separate.** Sync/freshness/API problems use a compact
   status area, not Signal cards.
7. **No fake freshness.** Market, account and retained-history ages are shown
   separately when materially different.
8. **Second-screen first.** A glance should be enough to understand the next
   action and return to the game.

## 3. Primary shell

Primary validation target: **1920×1080**. Use a fixed compact left navigation
and one main action column. The navigation and realized-performance block keep
the same screen position across normal, empty, degraded and corrective states;
only their contents may change.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│               │ Mes Signaux                         Profit réalisé            │
│               │ 2 signaux méritent votre attention Aujourd'hui   +3g 27s    │
│ Mes Signaux   │                                    30 j         +12g 48s ▾  │
│               │                                                              │
│ Artisanat     │ Marché actualisé il y a 45 s · prochaine actualisation ~15 s │
│   Bientôt     │ [Données ▾]                                                  │
│               │                                                              │
│ Réglages      │ [Signal 1]                                                   │
│               │ [Signal 2]                                                   │
│               │                                                              │
└───────────────┴──────────────────────────────────────────────────────────────┘
```

Do not use a rounded decorative footer. Prefer no footer for the product screen;
if a later implementation genuinely needs a persistent bottom status region, it
should be flat, quiet and functional.

Navigation decisions:

- `Mes Signaux` is selected by default and is the home destination.
- `Artisanat` is visible but disabled until implemented.
- Disabled helper copy: `Bientôt`.
- `Réglages` remains available and functional.
- Dashboard, Scanner, Investments, Inventory and Personal Learning do not appear
  in primary navigation.

## 4. Performance placement

Performance is contextual, not the main task.

Displayed hierarchy:

```text
Profit réalisé
Aujourd'hui     +3g 27s
30 j           +12g 48s  ▾
```

The disclosure may expand to:

```text
Aujourd'hui     +3g 27s
7 jours         +3g 21s
30 jours       +12g 48s
90 jours       +31g 02s
```

Rules:

- Today's realized profit and 30-day realized profit remain visible by default.
- 7-day and 90-day realized profit are hidden behind the compact disclosure.
- The performance block stays anchored in the same position across every Signals
  screen state so state changes do not move the owner's visual landmarks.
- Unrealized/open result, when available, is visually separate and never added
  into realized profit.
- Strategy breakdown does not occupy the default Signals header. It may live in
  progressive detail later.

## 5. Signal-card hierarchy

Default card:

```text
PLACER UN ORDRE D'ACHAT                                      Confiance élevée
Objet X

22 ×     MAX. 41s 27c

Profit modélisé : +2g 18s                                    [Pourquoi ?]
```

The action line plus quantity/price form the card's **execution block**. They
must be the strongest scannable content after the item name. Quantity and price
use larger/bolder typography than modeled profit so the owner can reproduce the
instruction in Guild Wars 2 with minimal visual search.

The execution block should deliberately echo the in-game Trading Post's order
entry grammar. For order/listing actions, use explicit field labels such as
`Quantité à saisir` and `Prix max. par unité` / `Prix de vente par unité`
rather than an abstract `22 × price` expression. The goal is visual transfer:
the owner should be able to look at Tyrian Ledger, then enter the same values in
the Trading Post without mentally reformatting them.

Money values should render as denomination groups with coin glyphs immediately
after each number, for example conceptually:

`12 [gold] 13 [silver] 19 [copper]`

instead of `12g 13s 19c`. Use locally controlled/CSS-rendered denomination
glyphs rather than adding a fragile community-asset dependency. Text/accessible
labels must still expose the full value semantically.

Required first-view fields:

1. concrete displayed action;
2. item image when available, with a stable fallback glyph when unavailable;
3. item;
4. quantity and relevant price/max price;
5. modeled result/profit;
6. confidence;
7. `Pourquoi ?`.

Card decisions:

- No ROI, spread, depth, history statistics or personal-learning table in the
  collapsed state.
- Confidence uses `Confiance élevée`, `Confiance moyenne` or
  `Confiance limitée`; no unsupported percentage.
- Modeled economics use `Profit modélisé` or `Résultat modélisé`, never
  guaranteed-profit wording. Modeled profit is useful supporting evidence, not
  the card's dominant value.
- Quantity and actionable price/max price have higher visual weight than modeled
  profit. Do not make the user search for the numbers they must type/click in game.
- For Trading Post actions, mirror the game's input order: quantity first, then
  per-unit price split into gold/silver/copper denominations.
- Price constraints use `Prix max. par unité` when the action has a maximum
  acceptable price; selling uses `Prix de vente par unité`.
- Modeled total profit stays visually secondary. A total purchase/listing amount
  may be shown only when it materially helps execution or bankroll awareness; it
  must not displace the per-unit values the owner enters in game.
- Cards are ordered by the backend's actionable ranking; the UI does not
  re-rank financial opportunities.

### Proposed displayed action copy

| Internal meaning | Displayed copy |
|---|---|
| Buy immediately | `ACHETER MAINTENANT` |
| Place buy order | `PLACER UN ORDRE D'ACHAT` |
| Update buy order | `METTRE À JOUR L'ORDRE D'ACHAT` |
| Cancel buy order | `ANNULER L'ORDRE D'ACHAT` |
| Craft | `FABRIQUER` |
| List for sale | `METTRE EN VENTE` |
| Relist | `REMETTRE EN VENTE` |
| Sell immediately | `VENDRE MAINTENANT` |
| Sell partial | `VENDRE PARTIELLEMENT` |

`REMETTRE EN VENTE` is preferred over the draft `RE-LISTER` because it is
clearer French while keeping the same structured backend action semantics.

## 6. Scenario A — zero Signals

Do not manufacture work.

```text
Mes Signaux

Aucun signal ne mérite votre attention pour le moment.
Les données actuelles ne justifient aucune action.

Marché actualisé il y a 52 s · actualisation automatique
[Données ▾]
```

Rules:

- No generic "explore opportunities" call to action.
- No raw candidate list.
- Performance remains visible.
- Operational status/freshness remains visible.
- The state must still look intentional rather than unfinished.

## 7. Scenario B — two simple actionable Signals

```text
Mes Signaux
2 signaux méritent votre attention

PLACER UN ORDRE D'ACHAT                                      Confiance élevée
Objet X

Quantité à saisir
22

Prix max. par unité
0 [gold] 41 [silver] 27 [copper]

Profit modélisé : +2 [gold] 18 [silver]                      [Pourquoi ?]


VENDRE MAINTENANT                                           Confiance moyenne
Objet Y

Quantité à vendre
8

Prix de vente par unité
0 [gold] 73 [silver] 14 [copper]

Résultat modélisé : +1 [gold] 06 [silver]                    [Pourquoi ?]
```

Rules:

- Cards remain visually equal; the first position conveys priority.
- Do not add large score badges.
- Quantity and actionable price must be readable without opening details.

## 8. Scenario C — many underlying candidates, few displayed

The default screen still shows only the attention-worthy subset.

```text
Mes Signaux
3 signaux méritent votre attention

[Signal 1]
[Signal 2]
[Signal 3]
```

Rules:

- Do not show "127 candidates rejected" in the normal surface.
- Do not add a `Voir tous les candidats` control in the MVP.
- Rejected/no-action candidates remain available only through future diagnostics
  if genuinely needed.
- The UI should comfortably fit about 3–5 compact Signals on a typical desktop
  second screen when that many are actually relevant; there is no quota forcing
  the engine to produce that number.

## 9. Scenario D — source freshness differs

Freshness is source-specific and compact.

Default compact state:

```text
Marché actualisé il y a 45 s · prochaine actualisation dans ~15 s
[Données ▾]
```

Expanded source detail:

```text
Marché
Actualisé il y a 45 s
Prochaine actualisation : ~15 s

Compte ArenaNet
Synchronisé il y a 3 min

Historique marché
Dernier échantillon enregistré il y a 2 min
```

Older account evidence:

```text
Compte ArenaNet
Synchronisé il y a 18 min · données anciennes
```

Rules:

- Prefer explicit labels `Compte ArenaNet` and `Historique marché`; avoid the
  ambiguous bare labels `Compte` and `Historique`.
- Show a next-refresh countdown only when the scheduler genuinely knows the next
  planned refresh time. Approximate values use `~`.
- When a refresh starts, replace countdown text with `Actualisation en cours…`.
- When there is no precise scheduled time, show `Actualisation automatique`
  rather than inventing a countdown.
- Do not use a progress bar merely to imply passage of time; it suggests a level
  of deterministic progress the scheduler/network may not guarantee.

- The backend/policy decides whether stale evidence invalidates a Signal.
- The UI never labels invalid/stale-beyond-policy data as current.
- If only some Signals depend on the stale source, hide/invalidate those Signals
  according to backend truth rather than globally inventing a healthy state.
- Do not collapse these sources into `Dernière mise à jour : ...`.

## 10. Scenario E — degraded or failed synchronization

Operational status uses a separate status strip above the action feed.

Example:

```text
⚠ Synchronisation du compte impossible.
Les signaux nécessitant l'état du compte sont masqués.
[Détails]
```

Alternative upstream outage:

```text
⚠ ArenaNet est temporairement indisponible.
Les données conservées affichent leur âge réel.
[Détails]
```

Rules:

- Never turn the outage itself into a Signal.
- Do not imply all displayed Signals remain valid if required evidence is stale.
- `Détails` may expose source/error category without credentials, raw payloads
  or unnecessary technical stack traces.
- Status meaning cannot depend on color alone.

## 11. Scenario F — urgent corrective action

Corrective actions may interrupt because they require a concrete manual action on
already committed resources.

```text
Prioritaire

ANNULER L'ORDRE D'ACHAT                                     Confiance élevée
Objet Z

Quantité concernée
120

Prix de l'ordre par unité
0 [gold] 18 [silver] 04 [copper]

Capital à libérer : 21 [gold] 64 [silver]

[Pourquoi ?]
```

Rules:

- `Prioritaire` is a visible text badge, not color-only urgency.
- The main label remains the actual hand action.
- A corrective action can surface even though the corresponding analytical state
  (for example an outbid or changed economics) would otherwise be internal.
- Do not use alarmist language.

## 12. Scenario G — expanded `Pourquoi ?`

`Pourquoi ?` expands inline beneath the same card. Avoid a modal so the owner
keeps the action and price in context.

```text
PLACER UN ORDRE D'ACHAT                                      Confiance élevée
Objet X

Quantité à saisir
22

Prix max. par unité
0 [gold] 41 [silver] 27 [copper]

Profit modélisé : +2 [gold] 18 [silver]

[Masquer les détails]

Pourquoi ce signal ?
• Pourquoi maintenant
  Marge et rotation suffisantes selon les données disponibles.

• Marché
  Profondeur compatible avec la quantité proposée.
  Historique retenu : données disponibles et suffisamment stables.

• Votre situation
  Quantité et capital compatibles avec les ressources non réservées.

• Risques et limites
  Le profit est modélisé ; le remplissage et le prix ne sont pas garantis.

• Fraîcheur des données
  Marché actualisé il y a 45 s.
  Compte ArenaNet synchronisé il y a 3 min.
  Historique marché : dernier échantillon enregistré il y a 2 min.
```

Rules:

- Explanations are driven by structured backend reasons/evidence.
- UI copy may summarize but may not recompute financial truth.
- Personal evidence appears only when it materially affected eligibility/ranking,
  with sample-size/recency limitations where applicable.
- Unknown evidence is stated explicitly rather than omitted or shown as zero.

## 13. Status vocabulary

Recommended displayed states:

| Meaning | Displayed copy |
|---|---|
| Loading | `Chargement des données…` |
| Account not connected | `Aucun compte ArenaNet connecté.` |
| Missing permission | `Autorisation ArenaNet insuffisante pour cette donnée.` |
| Never synchronized | `Cette source n'a pas encore été synchronisée.` |
| Retained stale data | `Données anciennes — âge affiché ci-dessous.` |
| ArenaNet unavailable | `ArenaNet est temporairement indisponible.` |
| Local sync error | `La synchronisation locale a échoué.` |

Keep the specific source name next to the state when ambiguity is possible.

## 14. Accessibility and interaction

- `Pourquoi ?` is a semantic button with `aria-expanded`.
- Suggested accessible label while collapsed:
  `Afficher pourquoi ce signal est recommandé`.
- Suggested accessible label while expanded:
  `Masquer l'explication de ce signal`.
- Disabled `Artisanat` remains perceivable and announces that the workspace is
  not yet available.
- Keyboard focus order follows navigation, status, Signals, then progressive
  detail.
- Status/confidence/urgency never depend on color alone.
- Values and action text retain WCAG 2.2 AA contrast.
- Avoid hover-only critical information.
- Narrow desktop/tablet layouts may stack performance/status above cards rather
  than horizontally compressing actionable prices.

## 15. Item imagery

Item imagery is desirable because it materially improves glance recognition.
The production card reserves a stable square image slot.

The upstream GW2 item contract may provide a full icon URL. #131 should extend
the existing typed item-display metadata boundary only after verifying and
documenting that field in the repository's endpoint contract. The frontend must
not construct render-service URLs itself.

When an icon is missing or fails to load, show a neutral item glyph without
changing card geometry. Missing imagery never blocks a Signal or changes
economic semantics.

## 16. Density decisions

For the MVP:

- one primary action column;
- compact left navigation;
- no dashboard-style grid of analytics;
- no persistent right-side inspector;
- validate the main layout at 1920×1080 before #131 implementation;
- fixed navigation and performance landmarks across all screen states;
- collapsed card target height roughly 110–150 px depending on wrapping;
- `Pourquoi ?` expands in place;
- performance occupies one compact header block;
- operational status consumes a row only when needed.

This keeps the screen useful around common second-monitor desktop widths without
requiring phone-first optimization.

## 17. Owner-review decisions

Owner-approved direction as of the UX spike review:

1. Keep the action-first composition rather than an analytics dashboard.
2. Keep the left vertical navigation and its position stable across states.
3. Keep qualitative confidence rather than a numeric score.
4. Use `REMETTRE EN VENTE` for relisting.
5. Keep `Prioritaire` calm and explicit for corrective urgency.
6. Keep performance in a stable compact header block.
7. Show `Aujourd'hui` and `30 j` by default; place `7 jours` and `90 jours`
   behind a disclosure.
8. Expand `Pourquoi ?` inline rather than opening a modal or side panel.
9. Replace terse freshness labels with explicit source meanings and expose detail
   behind `Données`.
10. Show truthful next-refresh timing when known; do not fabricate progress.
11. Keep `Artisanat · Bientôt` visible but disabled.
12. Preserve action-type color coding as a fast secondary cue, while retaining
    explicit action text so meaning never depends on color alone.
13. Reserve an item-image slot with a neutral fallback.
14. Remove the rounded decorative footer treatment.
15. Model Active/Passive execution paths later under #133; do not add them to the
    #130/#131 MVP interaction model.
16. Make the execution instruction the strongest card content: action plus
    quantity and price/max price outrank modeled profit in visual hierarchy.
17. Mirror the in-game Trading Post entry grammar for quantity and unit price,
    including separate gold/silver/copper denomination glyphs directly after
    their numeric values.

## 18. Acceptance mapping

- Zero Signals: section 6.
- Two simple Signals: section 7.
- Many candidates/few surfaced: section 8.
- Mixed freshness: section 9.
- Degraded/sync failure: section 10.
- Urgent corrective action: section 11.
- Expanded `Pourquoi ?`: section 12.
- Compact today/30d performance with 7d/90d disclosure: sections 3–4.
- Primary navigation: section 3.
- Item imagery/fallback: section 15.
- French UI copy: sections 3–15.
- No live ArenaNet dependency: all examples are static specification wireframes.
