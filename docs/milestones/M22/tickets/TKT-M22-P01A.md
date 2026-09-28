# TKT-M22-P01A — Aggregate Resource Demands

GitHub issue: #149

Milestone: M22. Risk: **R3**. Review: **SOL-GATED**.
Implementation: **Luna High explicitly permitted** for this bounded fix.
Fresh independent **Sol XHigh** review after required validation/CI is green.
Readiness: Ready only after TKT-M22-P00 / #148 is merged into develop.

## Outcome

A plan cannot bypass a resource limit by listing the same demand more than once.
Selection and direct durable start enforce the same combined demands against
remaining resources. Addresses audit **F01** only; not the whole P01 package.

## Entry points

- `src/Gw2Tp.Application/Plans/PlanOrchestrationService.cs`: `SelectWithin`, `BuildCapacities`, `OutstandingReservations` and current resource keys.
- `src/Gw2Tp.Application/Plans/PlanContracts.cs`: resource kind/identity, quantity/cash and start result contracts.
- `src/Gw2Tp.Infrastructure/Persistence/SqlitePlanRepository.cs`: `TryStartAsync`, `HasResourceConflict`, active reservations and transaction boundary.
- `tests/Gw2Tp.Application.Tests/PlanOrchestrationServiceTests.cs`.
- `tests/Gw2Tp.Infrastructure.Tests/SqlitePersistenceIntegrationTests.cs` (existing atomic-start and dependency-aware craft tests).
- `tests/Gw2Tp.Web.Tests/PlanEndpointMappingTests.cs` for regressions, not a second financial policy.

These are inspected entry points, not instructions to copy the current bug.
Re-read current develop before coding; dependencies may have changed line numbers.

## Implementation contract

1. Reproduce duplicate inventory requirements with independent expected values.
2. Introduce one checked deterministic aggregation policy keyed by **resource
   kind plus canonical identity**, shared by selection and durable start. Cash
   uses its canonical cash identity/amount; do not sum an irrelevant quantity as
   money. Do not merge distinct kinds sharing a textual ID.
3. Validate requirement totals before comparing or subtracting. Aggregate the
   candidate and existing outstanding reservations as appropriate. Reordering or
   splitting equivalent demands must not change admission.
4. Preserve `OutstandingReservations` produced-resource/residual semantics:
   purchases/earlier craft outputs may cover later inputs; do not naively sum all
   gross step inputs and reject valid acquire→craft→sell chains.
5. Preserve verified-state plus residual local-event projection, existing hard
   cash reserve, account isolation and idempotent start behavior.
6. Revalidate combined resources inside the existing durable start transaction;
   selection is advisory and cannot replace transactional checks. No partial
   record/reservation on failure, including overflow/invalid totals. Use existing
   typed failure conventions, or a minimal explicit extension if required.
7. A missing physical-inventory key in supplied capacity does not authorize an
   inventory demand. Preserve documented capacity rules for other resource kinds;
   do not redesign complete account ingestion in this ticket.

## Required regression vectors

| Case | Expected evidence |
|---|---|
| Same inventory key: 6 + 6, capacity 10 | Excluded by selection AND rejected through direct repository start |
| Same inventory key: 5 + 5, capacity 10 | Eligible and one durable start; split/reordered rows equivalent |
| Same textual identity under distinct resource kinds | Quantities never collapse together |
| Two candidates each require 6, capacity 10 | Selected bundle respects capacity; competing concurrent starts cannot both commit |
| Existing started plan reserves 4; new plan demands 4 + 4, capacity 10 | New start rejected; prior plan unchanged |
| Duplicate cash costs with a nonzero hard reserve | Combined cost cannot cross the reserve in either boundary |
| Checked overflow/invalid totals | Safe explicit failure, no negative availability or partial persisted start |
| Acquire→craft→sell using produced resources | Existing valid residual reservation tests continue to pass |
| Required inventory absent from supplied capacity | Cannot qualify; unrelated resource kinds retain documented semantics |
| Repeated same start identity | At most one durable start, preserving existing AlreadyStarted behavior |

Build consistent candidate steps and requirements in fixtures so a duplicate row
is not accidentally ignored by the repository's step-derived reservations.
Tests must exercise both public selection and repository start paths, not only
the aggregation helper. Include one invariant/property-style permutation/split
case where useful; avoid tests that merely duplicate the helper implementation.

## Validation commands

Run focused tests first in a supported .NET 10 environment:

```bash
dotnet test tests/Gw2Tp.Application.Tests/Gw2Tp.Application.Tests.csproj --filter FullyQualifiedName~PlanOrchestrationServiceTests
dotnet test tests/Gw2Tp.Infrastructure.Tests/Gw2Tp.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqlitePersistenceIntegrationTests
dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj --filter FullyQualifiedName~PlanEndpointMappingTests
```

Then run required repository CI. Record exact commands/results, including a
before-fix failing regression when practical. Do not claim a test passed when
SDK/environment startup failed. This ticket changes no external GW2 contract;
carry relevant VERIFY limitations without inventing new API guarantees.

## Non-goals and stop condition

No UI redesign, character inventory ingestion, partial-report/reconciliation
rewrite, passive continuation, repeat-execution lifecycle, schema cleanup, new
strategies or fee changes. Those require later bounded tickets. No owner decision
is outstanding for this scope. Escalate only a real new consequential ambiguity.

Open Draft PR targeting develop with `Closes #149` and actual M22 milestone.
Use `$tyrian-pr-review`; keep Draft until Sol XHigh APPROVE and green required
validation. Commit prefix `[TKT-M22-P01A]`. Record evidence and stop. Owner merges.
After merge, a separate checkpoint prepares the next ticket before #150; do not
implement #150 as a single Goal or claim other audit findings resolved.
