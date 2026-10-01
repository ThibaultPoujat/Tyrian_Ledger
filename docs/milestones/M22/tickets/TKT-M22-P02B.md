# TKT-M22-P02B — Equipment Protections and Crafting Actors

GitHub issue: #169

Milestone M22. Risk R3. Implementation: Luna High explicitly permitted.
Review NORMAL: independent Sol High using tyrian-pr-review.
Ready after #168 merges with its typed collector/coverage evidence; validate
the [B2 checks](../batch-02.md). This is a producer and deterministic policy
ticket; live admission/persistence is deferred to #171.

## Outcome and gap

Equipped/template equipment can never become a sale/craft input, and a crafting
capability always belongs to an actual character. Today the gateway merges
maximum discipline rating and any active flag across different characters:
an inactive level-500 actor plus active level-100 actor can fabricate active 500.

## Entry points

P02A's typed evidence collector/DTOs; AccountCraftingContracts.cs;
AccountCraftingGateway.cs; CraftingOpportunityPlanner.cs and
CraftingOpportunityService.cs (inspect consumer seams, defer live integration);
gateway/planner tests; VERIFY-016/017; relevant protection/crafting sections of
docs/specs/approved-product-direction.md and project-spec.md.

## Contract

1. Extend the collector with per-character crafting, equipment and all equipment
   tabs using documented schema/paths. Keep per-source/actor failures isolated,
   bounded fan-out and captured credential from P02A. Never treat only the active
   tab as complete template coverage. Read documented Legendary Armory locations
   conservatively; unknown location values are protected/unknown, not saleable.
2. Preserve each actor's discipline, rating and active state as one tuple. No
   merging maxima with another actor's active flag. Account recipe unlock facts
   keep their separate coverage. Unknown recipe or actor evidence is not a
   craftable plan. All characters remain considered even when not the selected
   in-game character. Character deletion/rename changes coverage honestly.
3. Implement pure deterministic protection evaluation over observed rows and
   explicit account-scoped protected-item/keep-quantity policy inputs. Always
   exclude equipped, inactive-template, armory and their attached components
   from consumable/saleable resources. Equipment observations are protection
   references, not additional physical inventory to add to bag counts.
4. Slot coordinates are not globally stable instance IDs. Where exact instance
   correspondence is unavailable, conservatively protect every otherwise
   matching item-ID candidate rather than choose an arbitrary copy. This can
   overprotect a spare; return a structured ambiguity reason. Bound actor/binding
   evidence remains attached to the instance; item ID alone cannot make a bound
   copy tradeable. Unknown binding/location/coverage never clears a protection.
5. Missing any actor's required equipment/template coverage blocks affected
   equipment-class candidates. A non-equipment commodity with positively known
   category and its own complete relevant protections can remain eligible;
   unknown category fails closed. Keep last known protected IDs as a protective
   floor on later partial reads within this invocation; persistent floor is #171.
   Never show account-wide protection success from one successful actor.
6. User-protected material IDs and minimum retained quantities apply across the
   account, not once per location. Protect all for an explicit full-item rule;
   retain 100 of observed usable 150 exactly once, leaving at most 50. This
   ticket's policy inputs are typed/local; durable settings UI and override
   workflow are outside scope. Equipped/template protections have no override.
7. Add deterministic actor selection for a recipe/chain: for every craft choose
   an actor whose own active discipline meets that recipe's minimum rating and
   whose required recipe evidence is adequate. If one actor cannot perform the
   entire proposed chain, return explicit actor assignments/change requirements
   or an unsupported-chain rejection. Do not fabricate a single actor. Stable
   ties use opaque local actor ID; no performance/economic ranking redesign.
8. Selection returns actor and required inventory access/transfer prerequisites,
   not teleportation or assumed cross-character accessibility. Bound-to-other-
   actor inputs are rejected; bank/material/shared facts retain access semantics.
   Do not issue a consuming instruction until #171's admission adapter proves
   the selected actor/resources. Existing live planner must not gain fictional
   actors; retain or conservatively disable unsupported legacy aggregate paths.

## Acceptance vectors

| Case | Required result |
|---|---|
| Inactive 500 on A, active 100 on B, recipe needs 400 | No active-500 actor; recipe rejected |
| Active 500 on A and B | Deterministic real actor selected; both retained |
| Chain needs two disciplines on different actors | Explicit changes/prerequisites or bounded rejection; no invented actor |
| Active, inactive-template, legendary/unknown armory locations | Always protected; no physical duplicate units |
| Equipped copy plus identical bag copy, no instance identity | All matching candidates protected; ambiguity reason |
| Missing tabs for one actor, known commodity on another | Equipment coverage degraded; only independently safe commodity usable |
| Unknown category/binding or actor mismatch | Affected candidate fails closed |
| 150 units across sources, account keep quantity 100 | At most 50 after protection; keep applied once |
| Actor rename/deletion or partial later response | No stale capability eligibility or dropped protection floor |
| Pure policy replay | Same assignments/protection reasons; no wall clock or HTTP |

Use independent expected vectors and a pre-fix regression for aggregate rating/
active mixing. Verify the protected-copy case at the policy's public boundary.

## Validation and exclusions

Focused new actor/protection tests plus all Crafting Application tests and
AccountCraftingGateway Infrastructure tests; node --test .github/scripts/*.test.mjs;
required CI. Extend VERIFY-016 only with evidence, preserve VERIFY-008/013/017.
Update #169's batch row and the concise policy/collector handoff documentation.

No UI redesign/forms, durable new account storage, live plan adapter, crafting
automation, expanded mandatory permissions, general plan timing or fees change.
No screenshot requirement for pure producer/policy changes. If existing French
rendered behavior materially changes, include its required visual evidence.

Commit [TKT-M22-P02B]; PR develop, Closes #169, milestone 11. NORMAL independent
Sol High; missing evidence means Draft. Stop for owner merge. Next is #170.
