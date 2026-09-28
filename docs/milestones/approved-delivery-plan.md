# Approved Corrective Delivery Plan

Approved direction: 2026-09-28. Baseline: develop `23be2fed09c6d5a42cfbd39f65d96831c30da526`. The package sequence is approved; only tickets explicitly marked Ready are executable. Packages are not single-ticket Goals. Operational order is [INDEX.md](INDEX.md) and GitHub #98.

## Implementation sequence

Work-package IDs below are planning identifiers, not existing GitHub issue numbers. Preserve the repository's isolated-worktree, ticket-level validation, review and owner-merge protocol. Assign review gates from the current model-effort authority when tickets are created; do not infer model gates from these package names.

| Order | Package | Deliverable | Exit criterion |
| --- | --- | --- | --- |
| 0 | P00 - Align product authority | Update navigation, session objectives, reconciliation contract and distribution assumptions across canonical docs; reconcile issue #98 and milestone index | No active instruction contradicts the approved product; mutable delivery state remains generated from its proper authority |
| 1 | P01 - Resource and execution correctness | Aggregated resource vectors, atomic start, idempotent reports, partial execution, passive continuation, settled/repeatable executions | A supported trade/craft completes, reconciles and repeats without overcommitment or duplicate effects |
| 2 | P02 - Complete account evidence and protections | Character inventories and disciplines, bank/material/delivery handling where supported, equipment/template protections, account epochs and provenance | An item can move between supported locations without duplication; protected instances never become candidates |
| 3 | P03 - Collection and local decision snapshots | Endpoint-specific caches, targeted preflight, background read models, priority/budget scheduling, short commit gates, temporal contracts | Reporting a completed step is fast and durable while unrelated API work is delayed; stale evidence cannot authorize a new commitment |
| 4 | P04 - Useful discovery and comparable economics | Rotating recipe/research universe, history coverage gates, shared utility, attention/capital constraints, both time objectives | Cross-strategy plans are comparable, respect the session and explain why alternatives do not qualify |
| 5 | P05 - Approved UI and outcome views | Shared shell, Signaux, preferences, comparison, active plan, Bilan, settings and recovery states | The owner can choose, execute, provisionally confirm and review a plan at 1920x1080 without interpreting internal engine diagnostics |
| 6 | P06 - Windows companion and distributable operation | Tray lifecycle, important notifications, permission test, installation/update/uninstall and local security/recovery validation | Supported alerts arrive with the UI closed; restart/resume preserves account scope, preferences, pending work and protections |
| 7 | P07 - End-to-end acceptance and release evidence | Scenario replay, fault injection, accessibility, performance budgets, clean installs and owner sessions | Release criteria pass with recorded evidence; realized value and time costs remain honestly attributed |

The sequence expresses dependencies, not a requirement to hide all UI progress until package 5. Once P00 defines typed contracts, a fixture-backed clickable UI can be developed as a bounded ticket and tested by the owner. It must remain clearly separated from live financial capability. Native notification feasibility should also be checked early so packaging limitations surface before release hardening.

P01 and P02 close their integration gate together: local resource correctness cannot be declared end-to-end correct while the account projection remains incomplete. P03's evidence contracts feed both P01 reconciliation and P04 decisions; introduce necessary interfaces early and deliver incrementally.

### First vertical slice

Before broad strategy expansion, demonstrate one real supported chain:

1. Player selects 15 minutes of active work, eligible activities and a capital percentage.
2. Assistant proposes a feasible, worthwhile plan with exact instructions and resource provenance.
3. Start reserves resources atomically.
4. Player performs a step and, if necessary, marks it completed locally.
5. Safe next steps proceed without an artificial five-minute pause.
6. Delayed API evidence confirms or corrects the local report without duplicate inventory or cash.
7. The outcome appears in Bilan with supported costs, fees and certainty.
8. The same strategy can be selected again as a new execution after appropriate settlement.

Repeat the slice with an acquisition that waits for a fill and with a partial/contradicted execution. Then validate the liquid-gold deadline objective using a suitable immediate-disposition path.

## 6. Audit traceability

| Finding | Owning package | Required proof |
| --- | --- | --- |
| F01 Duplicate resource requirements | P01 | 6+6 demand against 10 units fails both selection and atomic start |
| F02 Incomplete physical inventory | P02 | Acquired/collected/transferred item remains correctly available, including after shadow reconciliation |
| F03 Passive crafting dead end | P01 | Procurement transitions through verification into a repriced craft/sell continuation |
| F04 Completed source blocks repetition | P01 | Settled strategy can repeat; duplicate execution request remains idempotent |
| F05 Partial quantities leave stale dependencies | P01 | Remaining steps and reservations replan coherently |
| F06 Hidden execution price and invalid controls | P05 | Exact read-only values are visible; paused-state controls match server eligibility |
| F07 Incompatible utility scales | P04 | Equivalent economics/time compare consistently across strategy types |
| F08 Cold-start/history trap | P04 | A new installation has a bounded research queue and explainable eligibility path |
| F09 Expensive analysis inside commands | P03 | Local completion persists despite unrelated upstream delay |
| F10 Missing reusable cache | P03 | Metadata/recipe reuse works, stale action evidence is rejected, account scope cannot leak |
| F11 Amplified scans and long I/O gate | P03 | Request load is bounded; obsolete account reads cannot commit after clear/switch |
| F12 Fixed recipe prefix | P04 | Eligible candidates beyond the first prefix are eventually examined |
| F13 Coverage gaps treated as sufficient | P04 | Clustered observations and stale tails fail the appropriate quality gate |
| F14 Unknown basis blocks unrelated certainty | P02/P04 | Accounting uncertainty stays explicit while independently safe decisions remain possible |
| F15 TP accounting is not total strategy accounting | P01/P02/P05 | Transformations preserve basis/provenance and do not leave phantom holdings |
| F16 Screen-dependent volatile notifications | P06 | Delivery survives navigation/restart with deduplication and persistent preferences |
| F17 Coarse character/crafting capability | P02 | Selected actor can perform the step; unavailable capability cannot qualify |
| F18 Domain policy in web layer | P01/P03/P04 | Financial/candidate/time policies are testable outside HTTP; clock and contracts are consistent |
| F19 Missing cross-feature invariants | P07 and every relevant package | Replay/fault tests cover conservation, idempotency, lifecycle, epochs and contradictory evidence |

## 7. Verification and owner checkpoints

### Required scenarios

- Local completion followed by an unchanged cached response: remains pending, not falsely contradicted.
- Local completion followed by fresh compatible evidence: confirms once, with no duplicate effects.
- Partial confirmation: only the unverified remainder stays provisional.
- Contradiction after dependent work: affected chain pauses and exposes a recovery path without rewriting history.
- Double click, retry, out-of-order response and process restart: identical logical command has one durable effect.
- Passive order fill, collection, crafting, listing, sale and repeat: no lifecycle dead end.
- Inventory moves, bound items, character changes and equipment-template coverage: no phantom or unprotected resources.
- Preference or objective change during a plan: current execution remains coherent; proposed work is recalculated against remaining commitments.
- Capital budget lowered below existing exposure: no new commitment, clear explanation, no automatic game action.
- Short liquid-gold deadline with only uncertain future sales available: no falsely qualifying plan.
- API unavailable/rate-limited/partial: useful local state remains visible; new unsafe commitments stay blocked.
- Window closed, screen changed, restart, OS permission denied and resume from sleep: alert behavior is honest and consistent.
- Unknown cost basis or estimated time: Bilan does not present either as a verified profit or measured duration.

### Product checkpoints

1. Approve the clickable interaction flow with fixtures, including exceptional confirmation, both objectives and a contradiction state.
2. Run the first vertical slice against the owner's account with small configured exposure; validate correct behavior rather than chasing profit.
3. Run normal play sessions and record whether the actions were worth the time, whether notifications were justified and where the player hesitated.
4. Have a friend test a clean Windows installation, onboarding, private key handling, permissions, updates and uninstall before wider distribution.

All financial, persistence, resource, security and reconciliation changes require meaningful tests. Keep the existing review rules, and resolve the previously observed frontend runner exit issue in a clean supported environment. UI screenshot checks supplement accessibility and functional tests; they do not validate economics.


## Ready queue and checkpoints

- P00: TKT-M22-P00 / #148 freezes these authorities and references.
- First coding ticket: TKT-M22-P01A / #149, duplicate resource demand only; Ready after P00 merges.
- TKT-M22-G01 / #150 is a tracking gate, NOT an executable coding ticket. After #149, plan the next bounded ticket and insert it before this gate in both roadmap authorities. Keep the gate open until P01–P06 exits are evidenced and the owner accepts progression.
- Remaining P01 work must be split into idempotent reports/residual reconciliation, partial execution, passive continuation and settled/repeatable execution tickets with explicit contracts and review gates. Do not bundle them into #149.
- #96 remains the final security/recovery/release gate, after P01–P06. #97 verifies outcome evaluation; the Bilan UI and underlying accounting must already work before release acceptance.
- #139 external-history research is optional and cannot block resource/lifecycle corrections.
- A fixture UI ticket and Windows notification feasibility spike may be inserted early after dependency review. They require their own ready contracts; this does not authorize silently parallelizing the whole roadmap.

Each checkpoint records acceptance evidence, actual screenshots where relevant, unresolved VERIFY items and the next ready ticket. Product usability approval does not replace technical validation. AI prepares and maintains the contracts; the owner need not edit code or workflow files.
