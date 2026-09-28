# UX Contract

Status: owner-approved target, 2026-09-28. Runtime convergence is tracked through
[P00–P07](../milestones/approved-delivery-plan.md). The current implementation is
not evidence that every requirement below is delivered.

Read [approved product direction](../specs/approved-product-direction.md) for
session and reconciliation rules, and **open the relevant PNGs** in the
[visual baseline](tyrian-ledger-visual-reference.md) before any visual work.

## Navigation and attention

Primary rail: `Signaux / Plans / Bilan`, with `Réglages` at the bottom. Trading,
crafting and later supported strategies share plans; `Artisanat` is an activity,
not a top-level destination. Display French throughout, including errors and
accessibility text. Technical identifiers and repository prose remain English.

`Signaux` opens by default. Show two or three compact worthwhile opportunities,
plus a larger band only for an urgent actionable condition. The assistant is
always quiet; no game/silent/active mode switch. Waiting, harmless price changes
and unsupported recommendations do not become tasks or notifications.

Session controls are visible at the top. Bottom analysis status distinguishes
background health, account freshness and market freshness. Do not claim live
analysis when only a stale local snapshot is available. Empty is a valid state:
`Aucun signal ne mérite votre attention pour le moment.` Explain a binding
preference and suggest an optional change without changing it automatically.

## Session drawer

`Adapter ma session` opens the right drawer. Edits remain a draft until
`Appliquer à cette session`; closing/Escape discards uncommitted edits and
restores focus. Saving usual preferences is a separate explicit action.

Offer both objectives: active work within a duration, or liquid usable gold by
a deadline. Display the selected objective in the session summary, not only
inside the drawer. Suggestions: 15 minutes, 30% eligible capital, 2 po net profit
per plan and 0.5 po per active minute. Offer 15/30/50/custom percentages. Explain
these are starting settings, not expected returns. Show the backend-defined
capital base, ceiling, commitments and remainder. Existing commitments survive
session/preference changes. Reserve, downside tolerance and lock horizon remain
distinct controls. Activities are selectable per session only when supported.

The active-time objective allows later sale/fill and labels that delay. The
liquid-gold objective cannot qualify an uncertain future listing as available
cash by the deadline. Cash released from owned surplus and economic profit have
different labels and thresholds. Unknown basis never becomes zero cost.

## Plan comparison and start

`Voir le plan` opens a preview in `Plans` without reserving resources. Comparison
shows feasible alternatives for the session: modeled net gain or cash release,
active work, required capital, delay to cash, step/character count, confidence
and important uncertainty. A persistent right panel recaps the selected plan
with collapsible phases. `Pourquoi ce choix ?` uses backend reasons.

`Démarrer ce plan` runs targeted freshness/feasibility validation and reserves
resources locally and atomically. While checking, prevent duplicate submission;
on failure explain what changed and offer recalculation. The engine checks
compatibility across plans; the player never manages hidden resource conflicts.

## Active execution

Use grouped vertical phases left, current instruction center, recap right.
Long plans collapse completed/irrelevant phases; they do not grow a horizontal
stepper. Keep the chosen character, required location, exact quantity and exact
unit price/ceiling, fees and total visible where relevant. These are read-only
instructions computed by the backend. Copy controls copy safe text only.

Automatic API observation is primary. If delayed, offer the exceptional
`J’ai effectué cette étape` action using the displayed plan revision. Mark it
locally reported and pending verification, persist once, and allow safe next
steps. Do not add a five-minute pause to every step estimate. API confirmation
must have adequate fresh relevant evidence; elapsed time is not confirmation.
Material contradictions pause affected dependents and show guided recovery.

`Un problème avec cette étape ?` handles a mistaken declaration or different
actual action, preferably through evidence and guided correction. No routine
quantity/price form. Undo concerns a local declaration, never the in-game action.
Controls reflect actual server eligibility, including paused and invalid states.

## Bilan

Separate verified realized profit, cash released from surplus, active time and
remaining capital commitments. Show session/7-day/30-day windows where coverage
supports them. Unknown-basis sales, unclassified attribution, pending execution
and unrealized profits remain explicit and separate. Estimated time carries an
`Estimé` label. No fabricated lifetime totals or measured-time claims.

Plan results provide drill-down to supported costs/fees/evidence and calm
optional feedback (`Oui`, `Trop longues`, `Gain trop faible`). Feedback informs
reviewed evaluation; it does not autonomously change financial rules.

## Settings and protection

Tabs: `Compte et personnages`, `Protections`, `Alertes Windows`, `Données locales`.
Cover all characters' inventories and crafting disciplines, showing capability,
source freshness and missing permissions. Equipped/template equipment is always
protected from sale, salvage and consumption; additional item/material reserves
are configurable. Protection applies to instances/locations/binding correctly.

Expose native notification permission and a delivery test. Closing the UI keeps
the future tray companion running; quitting stops it. Explain OS permission,
sleep/offline limitations honestly. Never claim browser-only notifications can
work after its process exits. Alerts are important, actionable and deduplicated.

Retain API connection status, backup/restore, safe diagnostics and `Comprendre
mes calculs` under appropriate settings disclosures. Never display a stored key
or private technical payload merely to fill a mockup panel.

## Interaction acceptance

[Visual acceptance](tyrian-ledger-visual-reference.md#required-implementation-and-review-evidence)
is mandatory for UI PRs. Preview, preference edits and navigation never create
an in-game action. All live calculations are backend-authoritative. Keyboard,
contrast, visible focus, reduced motion and readable scaling are acceptance
requirements, not an optional polish pass.
