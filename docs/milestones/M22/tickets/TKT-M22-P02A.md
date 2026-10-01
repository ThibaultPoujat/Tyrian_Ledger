# TKT-M22-P02A — Source-Scoped Account Holdings Collection

GitHub issue: #168

Milestone M22. Risk R2. Implementation: Luna High explicitly permitted.
Review NORMAL: independent Sol Medium using tyrian-pr-review.
Batch B2, first implementation. Ready only after C03 / #167 merges and the
[B2 entry checks](../batch-02.md) hold. B1 predecessors are merged.

## Outcome and inspected gap

Create a typed collector for every enumerated character's bag inventory, shared
inventory, bank, material storage and TP delivery. Preserve source coverage and
location facts without treating them as spendable resources yet.

Baseline develop 020ca25dae4d44a804e060cdda48616b795d51e8. The crafting gateway
currently captures bank/material quantities but discards bank slot position and
character names; it does not collect character/shared inventories or delivery.
P01C's physical evidence intentionally stays Partial. Do not change that claim.

## Selective entry points

- Application/Crafting/AccountCraftingContracts.cs and AccountCraftingSnapshotService.cs
- Infrastructure/Crafting/AccountCraftingGateway.cs and AccountCraftingDtos.cs
- Infrastructure/Gw2Api request scheduler and PersonalTradingPost gateway patterns
- AccountCraftingGatewayTests and Gw2GatewayBoundaryTests
- VERIFY-008, VERIFY-016, VERIFY-017; B2 external evidence notes

Paths above are relative to src/ or tests/ as appropriate. Add a cohesive account
evidence namespace if useful; avoid putting external DTOs in Application.

## Contract

1. Add normalized account evidence contracts: trusted internal AccountScope;
   refresh identity and explicit evaluation time; roster and per-source/per-actor
   availability, completeness, coverage, fetch interval and nullable upstream
   observation provenance. Never use a snapshot's final fetch time as the clock
   for every source. Missing source/value and successful empty source differ.
2. Read one credential for an entire collector invocation, resolve its account
   identity once, and use that credential for every child request. Use bearer
   headers only. Keep secrets out of URLs, scheduler keys, logs, DTO/domain
   payloads, fixtures and persistence. Character names are local private facts,
   not secret keys; names may later appear in exact manual instructions but
   must not enter request logs or exported telemetry. Account IDs stay internal.
3. Enumerate the complete roster. Preserve stable local actor references and
   local display names; encode names correctly at the gateway boundary. Read
   each character independently. An unavailable roster means character coverage
   is unknown, not an account with zero characters. One failed actor does not
   erase successful actors or turn aggregate character coverage green.
4. Collect the documented read-only sources in B2's endpoint table. Preserve
   source/location, actor, bag/slot coordinates where supplied, item ID, positive
   count, binding and bound actor. An installed bag is not a saleable bag item;
   upgrades/infusions inside a slot are not loose holdings. Null bag/slot entries
   remain legitimate empties. Retain material zero rows and existing duplicate
   material normalization. Contradictory/malformed rows make that source partial
   or unavailable, never a silently reduced Complete collection.
5. Preserve delivery separately, including coins as integer copper. Repeated
   item IDs in delivery can be separate valid rows: retain row provenance and
   checked totals. Delivery is awaiting collection, never current wallet cash
   or immediately usable crafting stock. TP transaction history/current sell
   listings never fabricate physical holdings.
6. Reuse the existing scheduler, timeout, cancellation and retry policy. Bound
   character fan-out to at most four active character reads per invocation,
   with deterministic queued work; do not Task.WhenAll the whole roster with
   unbounded child work. Cancellation stops queued work. No new polling loop,
   completed-response cache, claimed ArenaNet quota or arbitrary character cap.
7. Retain location observations as observations, not globally stable instance
   IDs or transfer proof. Slot identity changes on movement; two identical
   items are not the same instance merely because item IDs match. Record the
   endpoint/schema contract and unknown coherence in VERIFY.
8. Keep the new collector disconnected from production polling, persistence,
   resource admission and frontend routes until P03A/P02C. Existing runtime
   snapshot behavior stays compatible. Expose a typed injectable seam for tests
   and the next ticket, not a new public diagnostic endpoint with raw account data.

Do not increase globally required API permissions. Optional missing grants
produce source-specific unavailability; no builds requirement or onboarding/key
mutation is introduced. New permission requirements need an owner decision.

## Acceptance vectors

| Case | Required result |
|---|---|
| Two characters, bank/shared/materials/delivery | All source/actor facts retained; no spendable aggregation |
| Character name with spaces, accents, slash | Correct path encoding; private name absent from logs/keys |
| One actor 403, another succeeds | Successful actor retained; missing permission explicit; roster incomplete |
| Roster unavailable or duplicate names | Unknown coverage / invalid payload; never zero-character Complete |
| Null bag/slot, installed bag, slotted infusion | Empties accepted; container/components not loose inventory |
| Delivery rows 3 + 4 of one item, 250c coins | Seven observed delivery units; zero wallet/usable credit |
| Negative count, overflow, unexpected binding | Source fails conservatively; no partial silent Complete |
| Credential changes halfway through mocked reads | Invocation uses its captured credential consistently |
| 30 actors, one stalled read, cancellation | At most four active character reads; queued work cancels |
| Same capture/replay, independently timed sources | No invented observation time or coherence assertion |

## Validation and exclusions

Run focused new collector tests plus:

```sh
dotnet test tests/Gw2Tp.Infrastructure.Tests/Gw2Tp.Infrastructure.Tests.csproj --filter 'FullyQualifiedName~AccountCraftingGatewayTests|FullyQualifiedName~Gw2GatewayBoundaryTests'
dotnet test tests/Gw2Tp.Application.Tests/Gw2Tp.Application.Tests.csproj --filter FullyQualifiedName~Crafting
node --test .github/scripts/*.test.mjs
```

Required CI also checks existing production/browser compatibility. Document the
typed handoff and update only #168's batch evidence row. No new UI/screenshots
apply. No global epochs, persistent holdings, protection policy, aggregation,
cost-basis inference, general scheduler redesign or gameplay/API writes.
Carry VERIFY-008/016/017 OPEN unless direct new evidence resolves a precise fact.

Commit [TKT-M22-P02A]; PR develop, Closes #168, actual milestone 11. Missing
independent review/required evidence means Draft. Stop for owner merge. Next
prepared contract is #169; verify its entry evidence rather than ask Astra.
