# Approved Product Direction

Status: owner-approved target, 2026-09-28. This contract defines intended behavior; it does not claim that the audited implementation already satisfies it.

Authority: this document owns session policy, reconciliation, protections and distribution intent. [UX](../ux/ux.md) owns interactions; [the visual reference](../ux/tyrian-ledger-visual-reference.md) owns appearance. Existing tested behavior is evidence of implementation, not permission to override the approved target. Financial/security invariants and verified upstream contracts remain binding.

## Product contract

Tyrian Ledger is a quiet, local-first Guild Wars 2 assistant for Windows, designed for a landscape 1920x1080 second monitor. It maximizes worthwhile opportunities within the player's chosen time, capital, activity and risk constraints. The player performs all in-game actions manually.

The owner does not write code. AI performs development, testing, documentation and technical investigation. Financial calculations remain deterministic; no runtime LLM is required. The owner reviews the product and performs final merges under the repository's existing delivery protocol.

The initial user is the owner. The application should be installable by friends and potentially distributed publicly after 1.0. Local-first describes where computation, credentials and private data live; it does not mean a private or permanently single-installation product.

### Approved information architecture

| Surface | Responsibility |
| --- | --- |
| Signaux | Two or three compact useful opportunities at a glance; expanded treatment only for genuinely urgent matters; session controls and background-analysis status |
| Session preferences drawer | Time objective, duration, capital percentage, worthwhile-gain thresholds, accepted activities, risk and capital-lock preferences |
| Plans | Compare feasible plans, preview grouped phases and choose a plan appropriate to the current session |
| Active plan | Exact read-only instructions, scalable grouped vertical steps, contextual recap, automatic observation and exceptional local confirmation |
| Bilan | Realized supported outcomes, cash released separately, outstanding commitments, estimated/measured time clearly distinguished |
| Réglages | Account and character coverage, protections, Windows notification readiness, credentials and local data/recovery |

Crafting is a strategy and capability within this model, not a competing top-level decision authority. The UI is French; repository prose, identifiers and machine-readable state remain English.

### Interaction rules

- `Voir le plan` opens a preview; it reserves nothing and starts nothing.
- `Démarrer ce plan` performs targeted validation and atomically reserves compatible resources locally.
- The assistant owns compatibility checks. The player must never be asked to reason about shared resource conflicts.
- Quantity, unit price/limit, fees and total commitment are computed and displayed as instructions, not editable execution fields.
- The current instruction stays stable while being followed. Material changes require an explicit updated instruction rather than silent reordering.
- Safe unchanged instructions can be copied, including item names. No in-game input automation is introduced.
- Normal waiting remains quiet. Notifications are reserved for urgent, material and actionable changes, not every profitable discovery or harmless undercut.
- The six generated views establish layout direction. Illustrative numbers, incidental icons, inconsistent background variants and generated wording are not executable specifications.

## 2. Session preferences and money constraints

The player may change preferences for each session and optionally save them as normal defaults. Starting suggestions are 15 minutes, 30% allocation, 2 gold minimum modeled net profit per plan and 0.5 gold per active minute. Offer 15%, 30%, 50% and a custom allocation. These are configurable product starting points, not promises of available returns or universally safe exposure.

Offer two explicit objectives:

1. `J'ai 15 minutes pour agir`: constrain active work, including setup, travel, character changes and interactions. Sales and buy orders may resolve later. Show that delay separately.
2. `Je veux récupérer de l'or sous 15 minutes`: require a feasible path to liquid, usable gold within the chosen horizon, based on current executable evidence. Do not qualify a plan solely because a future listing might sell. Include collection, travel and any unavoidable dependencies in the horizon. Acknowledge that changing markets can invalidate the estimate; do not promise a guaranteed deadline.

For the second objective, cash released from surplus and new economic profit remain separate quantities. Unknown original cost cannot become a fabricated profit merely to pass the gain filter. Label the relevant threshold according to the selected objective, and keep the net-profit criterion explicit where calculable.

The budget percentage applies to a defined, versioned eligible capital base. Display that base in gold, the allocation ceiling, existing commitments and remaining deployable capital. Protected assets and hypothetical sale proceeds are not freely spendable cash. Do not reset commitments on refresh, preference change, session restart or application restart. Lowering the ceiling below current commitments blocks additional risk and explains the excess; it does not invent an automatic liquidation instruction.

Capital allocation, untouched reserve, downside tolerance and acceptable lock duration are separate preferences. A scenario-loss filter or alert is not a guaranteed stop-loss. Keep conservative eligibility initially; expose suggestions with their reasons and uncertainty. If no plan qualifies, explain the binding preference and propose an optional change. Never silently relax it.

Activities are selected per session. All supported character inventories and crafting disciplines inform feasibility, while the chosen character and switching cost are visible in each plan. A strategy shown in settings must be implemented and verified; future conversions, salvage or event strategies are labelled unavailable rather than displayed as working filters.

## 3. Local confirmation and authoritative reconciliation

The owner approved an exceptional `J'ai effectué cette étape` button. It records completion locally using the displayed plan instructions, permits safe continuation, and is reconciled against later API evidence. It is not a claim of API-confirmed execution or verified actual price.

### State contract

| State | Meaning | UI and resource treatment |
| --- | --- | --- |
| Ready | Current instruction is eligible and fresh enough | Show exact instruction and optional local-confirmation action |
| Reported locally | User says the displayed step was completed | Mark `Déclaré effectué · vérification en attente`; persist provisional effects once |
| Partially evidenced | Upstream evidence accounts for part of the action | Apply only residual pending effects; re-evaluate remaining quantities |
| Confirmed | Compatible authoritative evidence supports the action | Replace supported provisional effects with verified state without double counting |
| Unresolved | Evidence is delayed, incomplete or cannot identify the action | Retain explicit uncertainty; do not claim confirmation |
| Contradicted | Sufficient relevant evidence materially conflicts with the local report | Pause affected dependent work, rebuild resources and explain recovery |
| Settled | Execution and required accounting/reconciliation are resolved | Archive outcome and allow a new execution of the same strategy |

### Required semantics

1. Persist an idempotent event with execution ID, step ID, plan revision, account epoch, time, displayed instruction and expected effects. Repeated clicks and retried requests have one effect.
2. Do not ask the player to fill quantity/price forms in the ordinary flow. A reported step uses planned values provisionally; later evidence supplies actual supported values.
3. Continue dependencies that are safe under the provisional resource model. Reserve provisional outputs for their intended continuation; do not make them available twice to unrelated plans.
4. Schedule verification according to the endpoint's relevant cache/freshness behavior. For transaction evidence with the documented five-minute cache, wait for a meaningful refresh opportunity. Five elapsed minutes is a retry point, not proof of freshness or completion.
5. A newly fetched response may still contain old state. An unchanged, pre-action or incomplete snapshot is not automatically a contradiction. Carry evidence provenance and freshness uncertainty.
6. The API is authoritative for the facts it actually exposes. Absence from an incomplete response is not proof of zero inventory. Do not fabricate a crafting transaction, exact cost basis or action identity that the API does not expose.
7. Match confirmations and partial fills to events without reusing the same evidence for multiple executions. Replace only the verified portion of provisional effects.
8. On a material contradiction, invalidate or pause affected descendants and replan from authoritative facts. Preserve an auditable history. Unrelated safe plans may continue.
9. Persist pending reconciliation through restart. Account change, restore and data-clear operations invalidate obsolete command contexts and refresh jobs.
10. Undo reverses a local declaration where allowed; it never claims to reverse an action in Guild Wars 2. If dependent actions already happened, require reconciliation rather than deleting history.
11. An exceptional `Un problème avec cette étape ?` path must handle accidental confirmation, a different quantity, or an action not completed as instructed. Prefer automatic evidence and guided correction; unresolved ambiguity cannot be papered over to preserve a progress bar.

### Timing consequences

Active-time estimates must not add a five-minute pause after every locally reported step. Distinguish active work, upstream verification, market waiting and unavoidable blocking. If a particular dependent action genuinely cannot proceed safely without verification, show that wait and exclude the plan from an incompatible time objective. Never improve estimates by simply ignoring a real dependency.

## 4. Permanent protections and notifications

Equipped equipment must never be suggested for sale, salvage or ingredient consumption. Include equipment used in templates in the protected model, and support explicit protected items/material reserves. If required equipment/protection evidence is missing, do not assume the affected assets are disposable. Scope protections to the correct item instances, binding and locations; do not accidentally block all tradable copies of the same item ID.

Windows notifications must operate independently of the visible screen and browser window. A local tray companion keeps the existing host and observation loop running when the UI closes, with clear close-versus-quit behavior. Startup behavior and notification permission are visible settings, and onboarding includes a delivery test.

Respect OS notification controls. A powered-off, sleeping or disconnected computer cannot provide the same live-monitoring guarantee as an awake connected one. Reconcile after resume, expire obsolete notifications, and avoid a burst of historical alerts. Closing the window must not be confused with explicitly quitting the assistant.

Urgency requires a meaningful user action and material consequence under current preferences. Stable IDs, event versions, cooldowns and persisted acknowledgements prevent repeated interruptions. Native notification clicks open the relevant current plan and revalidate it, rather than executing a stale action.
