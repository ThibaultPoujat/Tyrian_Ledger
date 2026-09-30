# TKT-M22-P01D — Partial Completion and Safe Residual Instructions

GitHub issue: #159

Milestone: M22. Risk R3. Luna High explicitly permitted for this bounded contract.
Review: NORMAL, independent Sol High inside the implementation session when
supported. No separate XHigh gate; self-review is insufficient.
Batch B1, second implementation. Ready after P01C / #158 and C02 / #157 merge.

## Outcome and inspected gap

Reporting fewer items than instructed must never expose the original full-size
dependent instruction. Actual local effects and unperformed remainder stay
distinct. This fixes bounded partial-execution safety, not all recovery planning.

Baseline e9712b4: ReportStep overwrites the current quantity, appends actual effects
and advances the ordinal without resizing downstream steps or validating a
non-craft over-report. OutstandingReservations then reasons from stale quantities.

## Entry points

PlanContracts.cs, PlanOrchestrationService.cs, PlanCompletionCommandService.cs
under src/Gw2Tp.Application/Plans; SqlitePlanRepository.CompleteStepAsync;
PlanEndpoints response mapping; their application/SQLite/endpoint tests.
Use the source-scoped evidence contract delivered by P01C. P01B command identity,
fingerprinting, atomic receipt/state commit and original-step retries remain binding.

## Supported behavior

1. Preserve original instructed quantity separately from actual reported quantity.
   One command closes that local reporting operation; it does not silently create
   a second attempt for an unperformed remainder. Quantity must be positive and
   no greater than the admitted instruction. Craft remains exact quantity only
   in this ticket. Over-report/invalid craft report fails without effects/receipt,
   with a structured recovery reason; subsequent API facts are not discarded.
2. Commit actual effects, updated dependency eligibility and reservations in the
   same completion transaction/receipt. For a partial report, never transiently
   expose the unadjusted downstream instruction. Preserve prior event identities,
   chronological history and immutable receipt interpretation.
3. Support exactly this automatic residual case: one BuyNow step followed by one
   List or SellNow step for the same item, with a one-to-one quantity dependency,
   no transform, no other resource/position/order dependency, and no downstream
   execution already recorded. Reporting 4 of 10 closes acquisition at 4,
   resizes the exit to 4, releases only the unspent acquisition commitment and
   recomputes required listing fee through the existing canonical fee policy.
   It does not place another order for the remaining 6. Check all hard resource
   constraints and never allocate provisional outputs twice to another execution.
4. For every other partial case (including multi-input craft chains, partial
   selling/listing, passive orders or already-acted downstream work), record the
   actual supported non-craft report but pause before any dependent action with
   a structured residual/recheck reason. Preserve conservative remaining
   reservations until an explicit safe resolution; do not guess a recipe scale,
   invent disposal, erase remaining inventory or declare the plan settled.
   General residual replanning is a later P01 package in the delivery map.
5. Recompute current/future-step state from dependency/resource checks; changing
   an instruction creates usable new revision/context so stale requests conflict.
   Preserve existing step IDs only when their identity remains the same; commands
   always bind the displayed revision. No automatic rebasing or silent retry.
6. Undo of the latest unconfirmed partial report restores the original instruction
   and reservations when no verified/acted dependent work makes that impossible.
   Otherwise retain history and enter reconciliation. An old completion receipt
   still acknowledges its historical operation and never reapplies its effects.
7. API partial confirmation of a fully reported step is not a smaller local
   report: retain only the unverified shadow, as defined by P01C. Do not resize
   the plan twice when local and verified quantities differ temporarily.
8. Return structured remaining quantities and paused eligibility for later UI
   adapters; do not add new quantity forms or redesign the existing panel.

## Required vectors

| Scenario | Result |
|---|---|
| Buy 4 of instructed 10, then single same-item exit | Exit exactly 4; no residual automatic purchase of 6 |
| Same partial command retried, restarted, or raced | One event/receipt; no repeated resize or resource release |
| Buy 4 at 100 copper; list 4 at 200 | Actual acquisition 400; fee reserve 40 under existing round-up policy; no 1,000-copper acquisition shadow |
| Zero, negative, over 10, or partial craft | Explicit rejection; no mutation or receipt |
| Buy feeds a multi-input craft / unsupported dependency shape | Actual acquisition recorded; dependent craft paused with honest remainder |
| Sell/List 4 of 10 | Actual effect for 4; remaining 6 neither deleted nor falsely settled; execution paused |
| Previously executed downstream step | No retrospective instruction/history rewrite; reconciliation required |
| Undo before dependent action | Original 10-unit instruction/reservations restored; receipt replay remains historical |
| P01C confirms 4 of a local report of 10 | Residual shadow is 6; not reinterpreted as a local report of 4 |
| Partial output and competing new plan | Only intended continuation can consume reserved provisional units |
| Overflow or transaction failure | No partially updated plan/receipt/reservations |

Prove public application behavior and real SQLite atomicity, including a pre-fix
regression. Preserve P01A/P01B/P01C vectors. If source contracts changed without
equivalent semantics, stop with the specific mismatch; routine symbol moves do
not require Astra.

## Validation / delivery

Run the three Plan/application, SqlitePersistenceIntegrationTests and Plan/web
commands from P01C, new partial/undo test classes, node --test .github/scripts/*.test.mjs,
frontend build and required CI. Test the existing French paused-state rendering
if mapping changes; any actual visual change requires the existing image/screenshot
contract. No screen redesign, new upstream endpoints, fee/retention changes,
passive continuation, settlement, broad resource allocator or full recipe replanner.
No new VERIFY fact is asserted.

Prefix [TKT-M22-P01D]; PR to develop, Closes #159, milestone 11.
Use $tyrian-pr-review with independent Sol High. Missing review/CI means Draft.
Record batch evidence; owner merges; stop. Next prepared ticket: P05A.
