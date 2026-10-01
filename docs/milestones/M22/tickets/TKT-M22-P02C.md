# TKT-M22-P02C — Conservative Protected Holdings Integration

GitHub issue: #171

Milestone M22. Risk R3. Implementation: GPT-6.1 Sol High.
Review NORMAL: independent GPT-6.1 Sol High using tyrian-pr-review.
Ready after #170 merges with green final-head CI/fresh Sol approval and #168/#169
typed handoffs. Validate B2 entry checks. Last implementation before #150.

## Outcome and gap

Persist and use the new location/protection/actor evidence through one application
projection. Trading/crafting admission stops summing overlapping portfolio and
bank/material representations. Delivery/history never creates phantom usable
stock. Safe quantities can be smaller than observed totals when coherence is
unknown; this ticket does not declare complete live reconciliation proof.

Today PlanEndpoints.VerifiedQuantities adds portfolio to bank/material facts;
BuildEvidenceFrame exposes one physical source as Partial. Crafting has its own
Owned projection. Replace divergent ownership authority, preserve P01C/D/P01B.

## Entry points

P02A/B collector/policy and P03A guarded commit seam; Application/Crafting
CraftingOpportunityService/Planner and snapshot service; Application/Plans
contracts/orchestration/resource admission; Web/Hosting/PlanEndpoints.cs and
loop decision composition; Application portfolio/account snapshot providers;
Infrastructure SQLite account repositories/schema/recovery; focused plan,
crafting, snapshot, endpoint and SQLite integration tests.

## Contract

1. Persist normalized per-source/actor observations, fetch provenance, coverage,
   protections and actor capabilities as account/generation-scoped evidence.
   Refresh publication is atomic under #170's fence. Failed sources remain
   explicitly failed/partial; keep prior data as stale/protective evidence only,
   never label it fresh Complete. Account clear/backup/restore/integrity checks
   include the new private tables. No secrets or raw API payloads in SQLite.
2. Define one deterministic application holdings projection used by resource
   admission, crafting Owned inputs and physical PlanEvidenceFrame adaptation.
   Separate observed quantities, usable-by-actor quantities, tradeable quantities,
   protected/unknown quantities and uncollected delivery. Source rows remain
   traceable. Portfolio cost/accounting lots are provenance, not extra inventory
   to add to observed physical quantities. Open sell listings/history are not
   bag stock. Binding and economic unknowns stay explicit.
3. Do not invent an atomic ArenaNet snapshot. While VERIFY-017 is OPEN and no
   external correlation proof exists, do not sum the same item across independent
   location captures for admission. For fungible eligible commodity observations,
   choose at most one currently qualifying physical location per item, with the
   largest usable count and deterministic source/actor/slot tie-break; reserve
   only that location and expose the omitted/ambiguous quantities. This is a
   conservative admission cap, not a guaranteed physical lower bound. All other
   location rows remain visible to the engine for later research/reconciliation.
4. Apply #169 protections/binding and account keep quantities to that admission
   cap once. Fail closed on stale/unknown relevant category/protection/source.
   No location can simultaneously own the same admitted resource via portfolio,
   shadow or another adapter. Global item reservations still coordinate all
   plans; capture selected location/actor in the commitment and revalidate it
   on start/resume/consuming-step eligibility. A moved or ambiguous source pauses
   affected instructions; it never silently spends another character's copy.
5. Delivery items/coins stay uncollected commitments. They do not increase wallet,
   tradeable or craftable stock; a Collect step may be modeled but auto-collect
   and passive continuation planning stay outside scope. On later bag evidence,
   the same item must not be credited once from delivery plus once from bag.
   Existing local Buy/Collect shadow remains provisional and isolated from
   observed stock; do not retire/re-add it twice on capture replay.
6. Preserve acquisition/cost provenance references without inventing cost or
   assigning a history purchase to a slot by item ID alone. A location move
   generates no buy/sell/profit event. Do not implement the general transfer or
   transformation basis engine: P02D is still Planned. Unknown owned cost remains
   unknown; keep integer copper and the canonical fee policy.
7. Live physical evidence remains Partial for negative reconciliation while
   cross-endpoint/cache coherence is unestablished. Even complete endpoint
   coverage is not a complete coherent account observation. Carry per-source
   clocks/coverage into the frame; never promote its maximum fetch timestamp or
   unchanged second read into freshness proof. Synthetic coherent frames can
   exercise existing P01C policy but cannot certify live completeness.
8. Integrate per-step real actor assignment from #169. The first supported live
   craft slice may use one actor and accessible bank/material/shared/own-bag
   inputs. A required cross-character transfer or multi-actor chain can be
   rejected with a structured unsupported-prerequisite reason; do not invent
   travel/time or silently omit a step. All characters still participate in
   discovery. No action instruction exceeds the selected eligible resources.
9. Wire the collector into the existing account refresh/loop through the guarded
   snapshot service. Reuse existing polling budget/scheduler; no independent
   per-character timer or global scheduler rewrite. Keep thin safe response
   adapters and unchanged preview provider. Existing production French degraded
   states may display structured reasons, but this is not P05C live-screen wiring.

## Acceptance vectors

| Case | Required result |
|---|---|
| Portfolio 10 + bank 10 representing one holding | At most 10 admitted, not 20 |
| Bag 10 → bank 10 with old bag response | At most 10, one selected source; no profit/acquisition event |
| Bank 6 + bag 4, coherence unknown | Observed 10; admission cap at most 6 before protections; explain limitation |
| Delivery 7 then bag 7 while delivery response lags | Delivery adds zero usable units; at most 7 admitted |
| Completed TP buy 10, bag not observed | No fabricated 10 physical units; local shadow remains separately provisional |
| Equipped/template matching copy or missing coverage | Protected / affected instruction blocked |
| Admission cap 150, keep quantity 100 | At most 50 admitted; account rule applied once |
| Actor A consumes B-bound inputs or inaccessible transfer | Rejected; no exact consuming instruction |
| Partial/stale source and TP refresh | No physical negative-observation progress or false Complete |
| Replay/restart, older capture after newer, concurrent completion | No duplicate stock/shadow retirement, progress regression or torn CAS |
| Snapshot persistence failure/old generation | Previous valid evidence preserved; no partial publication |
| Clear/restore account evidence | New tables covered; old work cannot repopulate them |

Include pre-fix public-boundary duplication regression and real SQLite restart,
partial source and rollback evidence. For existing money vectors, expectations
come from canonical policy, never a duplicated ad-hoc fee formula.

## Validation and exclusions

New holdings/protection/actor integration classes plus Application Plan/Crafting
tests, Infrastructure AccountCrafting/SQLite persistence/recovery tests, Web Plan
mapping/account refresh/loop tests; frontend tests/build if payloads change;
node --test .github/scripts/*.test.mjs and required CI. Update the architecture
ownership/provenance section and #171 batch row. Preserve VERIFY-008/013/016/017.

No general transfer-cost engine, settlement/repetition, passive continuation,
general residual replanner, native alerts, preferences/economics redesign, runtime
LLM or approved-screen redesign. If rendered production UI changes, provide the
required original/actual 1920×1080 evidence; otherwise state API/domain-only. Deferred action-relevant transfer/coherence and supported instance
correspondence are owned by Planned P02-E in the remaining map; this contract
never certifies those exits.

Commit [TKT-M22-P02C]; PR develop, Closes #171, actual milestone 11. NORMAL GPT-6.1 Sol
High independent review; missing evidence means Draft. Stop after owner merge
at #150 for the next batch preparation. B2 does not close all P01–P06 exits.
