# TKT-M22-P01C — Source-Scoped Reconciliation Evidence

GitHub issue: #158

Milestone: M22. Risk R3. Implementation: Luna High explicitly permitted.
Review: SOL-GATED, fresh independent Sol XHigh after green validation/CI.
Batch B1, first implementation. Ready after C02 / #157 merges; #154 is merged.

## Outcome and inspected gap

A local completion remains provisional when the API supplies old, incomplete or
unrelated evidence. Confirmation retires only the evidenced shadow effects;
material contradiction pauses the affected execution without rewriting history.

Baseline: develop e9712b4e97b547550ed73f33681af3a81be76d39.
PlanEndpoints combines wallet/portfolio, bank/material and TP captures, but passes
one TP capture timestamp and TP completeness set to reconciliation. The craft
branch in ReconcileWithVerifiedState counts negative inventory observations using
that TP clock and treats missing quantity keys as zero. A successful TP refresh
does not establish complete fresh physical inventory. The current account model
does not include all character inventories; this ticket must represent that gap.

## Read / edit entry points

- src/Gw2Tp.Application/Plans/PlanContracts.cs
- src/Gw2Tp.Application/Plans/PlanOrchestrationService.cs
- src/Gw2Tp.Web/Hosting/PlanEndpoints.cs (BuildContextAsync, ReadEvidenceAsync, ReconcilePlansAsync)
- src/Gw2Tp.Application/Crafting/AccountCraftingContracts.cs and snapshot service
- src/Gw2Tp.Application/PersonalTradingPost/PersonalTradingPostSynchronizationService.cs and synchronization contracts
- src/Gw2Tp.Infrastructure/Persistence/SqlitePlanRepository.cs only for durable evidence progress/CAS support
- PlanOrchestrationServiceTests, PlanEndpointMappingTests and SqlitePersistenceIntegrationTests

Read the reconciliation section of docs/specs/approved-product-direction.md and
VERIFY-008 (cache uncertainty). Preserve P01A aggregation and P01B receipts/guards.

## Bounded contract

1. Define a typed application evidence frame with trusted account scope and
   separate provenance for physical inventory, cash, current orders and completed
   transactions. Each required source records capture identity/time, availability,
   completeness/coverage and relevant normalized evidence. Transport fetch time
   and upstream observation time must not be conflated. Unknown provenance stays
   unknown. Shared atomic TP synchronization may supply shared provenance for its
   four completely read endpoints; it cannot certify other sources.
2. Map existing source facts honestly. Failed/stale/absent/partial source data is
   not an empty complete collection. Current bank/material-only aggregation is
   incomplete for account-wide physical inventory; do not assert completeness
   until a later P02 producer supplies it. This may keep craft confirmation
   pending while positive identity-bearing TP evidence still reconciles trading.
3. Reconciliation receives explicit UTC evaluation time and evidence provenance;
   no wall-clock reads inside deterministic policy. Reject other-account frames;
   ignore out-of-order/same-capture inputs for new negative-observation counts.
   Reading the same capture twice, even after restart, cannot make it two captures.
4. Match positive evidence only to compatible action/item/quantity/identity and
   the instruction/event time boundaries. Never reuse an evidence identity for
   two events in the execution. A complete net craft projection may confirm only
   with adequate physical inventory coverage/time for every affected key; a TP
   capture or a listing alone cannot prove craft consumption.
5. Negative inventory evidence requires complete relevant inventory coverage and
   its own qualifying observation progress. Unchanged cached/unknown-freshness
   content and elapsed time alone cannot prove contradiction. Keep the existing
   conservative TP cancellation observation policy and late-fill reopening; do
   not replace it with a claim that five minutes guarantees upstream freshness.
   If provenance cannot support a negative inference, remain AwaitingEvidence.
6. Partial positive evidence retires only its verified portion. Repeated/older
   frames never resurrect residual effects or move verified quantities backward.
   Do not change the canonical fee policy. Contradiction retains events/receipts,
   prevents dependent instructions, and exposes a structured reason.
7. Persist evidence-consumption progress with the plan's existing CAS semantics.
   Restart/replay yields the same result. Do not save an unchanged reconciliation
   result merely because the screen was refreshed (avoid false command conflicts);
   compare semantic durable content, not new collection reference identities.
   On a concurrent completion, reread or defer; never overwrite its committed event.
8. Thin endpoints adapt source facts; matching, eligibility and contradiction
   policy belong to Application. Do not introduce a second policy in React.

The evidence-frame shape is the stable handoff for later P02/P03 producers.
Publish its source/completeness semantics in the relevant architecture section,
including the conservative behavior of current incomplete producers.

## Acceptance vectors

| Case | Required evidence |
|---|---|
| Old bank/material capture; two new TP captures | Craft stays provisional, no inventory contradiction increment |
| Missing physical source or missing key in partial frame | Unknown, never authoritative zero |
| Complete synthetic inventory frame with matching craft input/output deltas | Confirms once; repeated frame has no further effect |
| Listing observed but inventory coverage incomplete | Listing may confirm; craft consumption remains unproven |
| Same capture replayed after repository reopen | No second negative observation, effect or semantic revision increment |
| Older capture follows newer partial confirmation | Verified amount and residual projection do not regress |
| Quantity 10 locally reported, verified 4 then 10 | Only 6 remains provisional after 4; zero after 10; no double cash/inventory |
| Unchanged/unknown cache freshness beyond five minutes | Pending; elapsed time is not API truth |
| Exact late cancellation fill / unrelated item change | Existing cancellation conflict works; unrelated change cannot contradict craft |
| Other-account evidence | No mutation or cross-account disclosure |
| Completion races reconciliation | CAS preserves completion/receipt; stale write cannot replace it |
| Full unchanged read/refresh | No redundant plan Save/revision change |

Include pre-fix failing regressions at the public orchestration/endpoint boundary,
plus real SQLite restart/concurrency evidence. Synthetic completeness proves the
policy, not live API completeness. Existing tests that codify false inventory
certainty must be replaced with these stronger assertions, not simply deleted.

## Validation and exclusions

Run:
- dotnet test tests/Gw2Tp.Application.Tests/Gw2Tp.Application.Tests.csproj --filter FullyQualifiedName~Plan
- dotnet test tests/Gw2Tp.Infrastructure.Tests/Gw2Tp.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqlitePersistenceIntegrationTests
- dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj --filter FullyQualifiedName~Plan
- new affected source-provenance test classes if not covered above
- node --test .github/scripts/*.test.mjs
- required CI, including frontend/browser compatibility checks

No new ArenaNet endpoints/permissions, complete inventory ingestion, scheduling,
cache implementation, global epochs, partial replanning, settlement, UI redesign
or automatic gameplay. Carry VERIFY-008 forward; no new cache guarantee.
No retention-policy change. Update batch evidence with PR/head/tests/review.

Commit prefix [TKT-M22-P01C]; Draft PR to develop, Closes #158, milestone 11.
Use $tyrian-pr-review; owner merges. Stop after this ticket. The next prepared
ticket is P01D; no Astra checkpoint is needed if its prerequisites still hold.
