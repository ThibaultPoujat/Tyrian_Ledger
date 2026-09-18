# Signals Second-Screen UX Spike

GitHub issue: #130

Status: **Draft for owner review**

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
3. **Compact before complete.** The first view answers what to do, with what,
   how much, at what price, and why it is worth attention.
4. **Truthful uncertainty.** Modeled results are labeled as modeled; confidence
   is qualitative rather than false numeric precision.
5. **Operational truth is separate.** Sync/freshness/API problems use a compact
   status area, not Signal cards.
6. **No fake freshness.** Market, account and retained-history ages are shown
   separately when materially different.
7. **Second-screen first.** A glance should be enough to understand the next
   action and return to the game.

## 3. Primary shell

Desktop target: compact vertical navigation plus one main column. A narrow
secondary header region carries performance and source status without competing
with the action feed.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Tyrian Ledger                                  Profit réalisé · 30 j +12g 48s│
│                                                7 j +3g 21s · 90 j +31g 02s  │
├───────────────┬──────────────────────────────────────────────────────────────┤
│ Mes Signaux   │ Mes Signaux                                                   │
│               │ 2 signaux méritent votre attention                          │
│ Artisanat     │                                                              │
│   Bientôt     │ Marché : 45 s · Compte : 3 min · Historique : 2 min         │
│               │                                                              │
│ Réglages      │ [Signal 1]                                                   │
│               │ [Signal 2]                                                   │
│               │                                                              │
└───────────────┴──────────────────────────────────────────────────────────────┘
```

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
Profit réalisé · 30 j     +12g 48s
7 j +3g 21s · 90 j +31g 02s
```

Rules:

- 30-day realized profit is the only headline number.
- 7-day and 90-day realized profit are secondary.
- Unrealized/open result, when available, is visually separate and never added
  into realized profit.
- Strategy breakdown does not occupy the default Signals header. It may live in
  progressive detail later.

## 5. Signal-card hierarchy

Default card:

```text
PLACER UN ORDRE D'ACHAT                                      Confiance élevée
Objet X

22 × max. 41s 27c
Profit modélisé : +2g 18s

[Pourquoi ?]
```

Required first-view fields:

1. concrete displayed action;
2. item;
3. quantity and relevant price/max price;
4. modeled result/profit;
5. confidence;
6. `Pourquoi ?`.

Card decisions:

- No ROI, spread, depth, history statistics or personal-learning table in the
  collapsed state.
- Confidence uses `Confiance élevée`, `Confiance moyenne` or
  `Confiance limitée`; no unsupported percentage.
- Modeled economics use `Profit modélisé` or `Résultat modélisé`, never
  guaranteed-profit wording.
- Price constraints use `max.` when the action has a maximum acceptable price.
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

Marché : 52 s · Compte : 4 min · Historique : 2 min
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
22 × max. 41s 27c
Profit modélisé : +2g 18s
[Pourquoi ?]

VENDRE MAINTENANT                                           Confiance moyenne
Objet Y
8 × 73s 14c
Résultat modélisé : +1g 06s
[Pourquoi ?]
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

Normal example:

```text
Marché : 45 s · Compte : 3 min · Historique : 2 min
```

Older account evidence:

```text
Marché : 38 s · Compte : données datant de 18 min · Historique : 2 min
```

Rules:

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
120 × 18s 04c
Capital à libérer : 21g 64s

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
22 × max. 41s 27c
Profit modélisé : +2g 18s

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
  Marché : 45 s · Compte : 3 min · Historique : 2 min
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

## 15. Density decisions

For the MVP:

- one primary action column;
- compact left navigation;
- no dashboard-style grid of analytics;
- no persistent right-side inspector;
- collapsed card target height roughly 110–150 px depending on wrapping;
- `Pourquoi ?` expands in place;
- performance occupies one compact header block;
- operational status consumes a row only when needed.

This keeps the screen useful around common second-monitor desktop widths without
requiring phone-first optimization.

## 16. Owner-review checkpoints

Before #131 implementation, validate these proposed decisions:

1. The screen is action-first enough and does not feel like another analytics dashboard.
2. Qualitative confidence labels are preferable to a numeric score.
3. `REMETTRE EN VENTE` is acceptable displayed copy for relisting.
4. `Prioritaire` is sufficient for corrective urgency without alarmist styling.
5. Performance belongs in the compact header rather than below the Signal list.
6. `Pourquoi ?` should expand inline, not open a modal/side panel.
7. Showing source-specific age in one compact line is understandable.
8. Disabled `Artisanat · Bientôt` is preferable to hiding the destination.

## 17. Acceptance mapping

- Zero Signals: section 6.
- Two simple Signals: section 7.
- Many candidates/few surfaced: section 8.
- Mixed freshness: section 9.
- Degraded/sync failure: section 10.
- Urgent corrective action: section 11.
- Expanded `Pourquoi ?`: section 12.
- Compact 30d/7d/90d performance: sections 3–4.
- Primary navigation: section 3.
- French UI copy: sections 3–14.
- No live ArenaNet dependency: all examples are static specification wireframes.
