# Account Holdings Collector and Admission — B2 Handoff

P02A / #168 adds a disconnected producer for B2. It does not persist, poll,
reserve, value or admit resources, and has no browser endpoint. P02B extends
protection/actor evidence; P03A guards account/store publication before P02C
integrates the collector. P02B also corrects the legacy mixed rating/active summary
conservatively, without adding an actor or protection admission adapter.

## Typed seam and composition

`IAccountHoldingsCollector.CollectAsync(evaluatedAtUtc, cancellationToken)` returns
an internal `AccountHoldingsCapture` only after resolving an `AccountScope`.
Credential/identity failure returns a safe gateway error; individual source
failure remains inside a successful capture. `IsPartialData` flags an incomplete
source/character coverage, not cross-source physical coherence. Neither false
nor complete endpoint results certify a coherent account observation.

The explicit opt-in `AddTyrianLedgerAccountHoldingsCollector` extension requires
the existing account connection's credential source, clock and scheduler. It
creates its own named, header-redacted HTTP client with all HTTP loggers removed.
P02A/P02B did not enable production composition; P02C now invokes it through
the guarded snapshot service described below. Tests can inject the typed
interface; infrastructure tests construct the gateway with synthetic inputs.
There is no public key parameter or raw-response DTO in Application.

One invocation reads the credential once and resolves the account once. Every
child/retry uses that captured credential. A unique nonsecret refresh ID scopes
scheduler request keys; keys contain safe operation/index values, never names,
account IDs or credential fingerprints. Cross-invocation coalescing is deliberately
absent so observations and their fetch intervals cannot be confused. The existing
scheduler owns capacity/rate limits and configured retry/backoff; there is no new
completed-response cache or polling loop. Character reads use four sequential
workers over an ordinal-name queue. Cancellation stops queued work and propagates.

## Evidence semantics

Each endpoint/actor has availability, completeness, source/location coverage,
its own local fetch start/end interval (including queue/retry), and nullable
upstream observation time. Upstream observation time is always unknown here;
no local finish time, HTTP Date or Last-Modified is converted into physical
observation time. The caller supplies evaluation time independently.

Roster failure means unknown expected actor coverage. A valid empty roster is
Complete with zero expected actors. One failed actor retains the other actors
and makes aggregate inventory coverage Partial. Malformed/contradictory sources
are unavailable with Partial completeness and no reduced usable value; permission
and transient errors are source-specific, with unknown location counts.

Actors retain local private display names and opaque deterministic references
from length-delimited account/name input. These are not credentials or globally
immutable character IDs. Reordering does not change a reference; account change
or rename does. Bound-to names use the same reference rule, even if that actor
is absent from the current roster. No correspondence or access is inferred.

A location is a coordinate within a capture, never transfer proof or a stable
physical instance ID. Inventory observations preserve bank/shared slot indices
and character bag/slot coordinates. Empty positions contribute to coverage but
not item quantities. Installed bags and attached upgrade/infusion references
are distinct from loose holdings. Attached component indices are their returned
array positions, not inferred equipment-slot indices.

Materials preserve zero rows and normalize repeated IDs only when quantity and
binding agree. Category IDs and raw row indices remain attached to the one
normalized observation, without adding the duplicate quantity. Unknown binding,
missing character-bound name, contradictory binding, invalid item/count/component,
bag-size mismatch or checked-total overflow rejects the affected source.

Delivery retains row indices, repeated item IDs, checked per-item totals and
nonnegative 64-bit integer copper. Delivery has a separate type: it is awaiting
collection, never wallet money or usable crafting/trading stock. No transaction
history, current sell order or portfolio lot is collected as physical stock.
No source quantities are summed across independently captured locations.

## Endpoint/schema contract and remaining uncertainties

All requests are GET with bearer headers and existing pinned schema
`2025-08-29T01:00:00.000Z`. This is a requested version, not evidence of a live
authenticated schema/coverage probe. Synthetic fixtures exercise documented shapes.

| Source | Documented source | Typed facts |
|---|---|---|
| Roster | [characters](https://wiki.guildwars2.com/wiki/API:2/characters) | Complete requested name list; per-actor coverage |
| Character inventory | [inventory](https://wiki.guildwars2.com/wiki/API:2/characters/:id/inventory) | Nullable bags/slots, installed container, item binding/bound-to, components |
| Shared inventory | [account inventory](https://wiki.guildwars2.com/wiki/API:2/account/inventory) | Nullable shared slots; optional Account binding |
| Bank | [bank](https://wiki.guildwars2.com/wiki/API:2/account/bank) | Nullable bank positions; binding/bound-to and components |
| Materials | [materials](https://wiki.guildwars2.com/wiki/API:2/account/materials) | ID/category/count, including zero; optional Account binding |
| TP delivery | [delivery](https://wiki.guildwars2.com/wiki/API:2/commerce/delivery) | Uncollected coins and repeated item rows |

Optional permission failure degrades only that source. No mandatory grant or
onboarding change is introduced. VERIFY-008/016/017 stay OPEN: endpoint success
is not proof of cache freshness, atomic cross-source coverage, immutable instance
correspondence or transfer correlation. P01C live physical evidence remains Partial.

## Verification and next-ticket use

`AccountHoldingsGatewayTests` contains synthetic endpoint fixtures and independent
expected vectors for source isolation, names/path privacy, containers/components,
delivery 3+4/250c, material duplicates/zeros, malformed/overflow/binding failures,
credential capture, per-source timing/replay, roster coverage, 30-actor cancellation
and progress behind a stalled actor, real scheduler retries, timeout/capacity,
opt-in composition and HTTP log suppression. No live account data is required.

Run the collector tests and the #168 contract's existing Infrastructure crafting/
gateway-boundary, Application Crafting and workflow tests. There is no UI change
or screenshot/preview requirement. The next ticket consumes this typed seam after
#168 merges with reviewed head and green final-head CI; it must not enable live
admission from these observations alone.

## P02B equipment and crafting handoff

Each `ActorHoldingsEvidence` now includes independently timed crafting tuples,
`Equipment`, `EquipmentTabRoster` and `EquipmentTabs`. Account recipe unlocks
have their own source evidence. Five sequential child reads per worker (inventory,
crafting, equipment, tab list, all tabs) retain the four-read ceiling even with
optional failures. Requests use the same captured credential and encoded private
name. `equipmenttabs?tabs=all` is checked against the separately enumerated tab
IDs and equipment tab references; active-only/missing/duplicate/inconsistent tabs
cannot establish complete protection coverage. An unavailable expected tab list
still permits retaining protective references from an all-tabs response as Partial.

The documented [crafting](https://wiki.guildwars2.com/wiki/API%3A2/characters/%3Aid/crafting),
[equipment](https://wiki.guildwars2.com/wiki/API%3A2/characters/%3Aid/equipment) and
[equipment-tabs](https://wiki.guildwars2.com/wiki/API%3A2/characters/%3Aid/equipmenttabs)
shapes were inspected again on 2026-10-01 via indexed official wiki content;
direct page retrieval returned HTTP 403. No authenticated schema/permission probe
was performed. The existing pinned version is requested on every read. Equipment
keeps row/slot/tab provenance, binding/bound actor, optional armory unlock count
and attached components. Unlock count is a reference fact, not physical stock.
`Equipped`, `Armory`, `EquippedFromLegendaryArmory` and `LegendaryArmory` are all
protected. Missing/future locations or bindings retain references as Partial;
invalid IDs/counts/components fail the source. No extra mandatory grant is added.
The equipment-tabs infobox/notes permission discrepancy remains VERIFY-016.

`AccountHoldingsProtectionPolicy.Evaluate` is pure and disconnected. Inputs are
this account's capture, explicit full-item/minimum-retained rules, positively
known item categories, optional consuming actor, and an optional same-account
invocation floor from the previous evaluation. A future caller must feed the
returned floor into later partial reads in the same invocation. The floor unions
equipment and attached-component IDs; it never carries crafting capabilities.
It is not persistent state: durable floors/epoch invalidation belong to #171/#170.

Equipment is never added to physical holdings. Without established instance
correspondence, every matching loose candidate is blocked with
`EquipmentReferenceAmbiguous`, including spares and loose copies of attached
components. Missing any expected actor's required equipment/template coverage
blocks equipment-class candidates account-wide; unknown category fails closed.
A positively identified commodity with complete own inventory/rule evidence may
retain a protection allowance even when another actor's equipment source fails.
An invalid binding or actor, unknown location or incomplete relevant source never
becomes eligible. Bound quantities retain their actor restriction and have zero
sale allowance. Explicit full-item rules always protect; a keep quantity is
subtracted once per item across otherwise usable observations in stable location
order. The 70 bank + 80 material / retain 100 vector leaves only 50. These are
**allowances over observations**, not spendable totals, tradeability proof or a
coherent physical account projection. #171 must impose its independent-source
physical cap and admission constraints before a plan consumes anything.

`CraftingActorSelector.Select` considers the current complete roster and each
actor's own complete active discipline/rating tuples. Recipe unlock evidence is
separate and required. Stable ties use opaque actor ID. The selector requires a
single real actor capable of the whole chain; a chain possible only across actors
returns `UnsupportedChain`. This bounded rejection is intentional until switching
is modeled. Complete successful actors can remain candidates when another actor's
crafting read fails; missing roster or stale/deleted/renamed actor facts cannot
create an eligible actor. Input evidence must belong to a complete source of this
capture. Bound-to-other-actor inputs reject; bank/material/shared access and
unbound cross-character transfers remain explicit prerequisites. Every successful
selection includes `AdmissionRequired` and produces no consuming instructions.
At the P02B boundary the live planner was not wired to this selector/policy;
P02C supplies the conservative integration described below.

The legacy snapshot cannot persist actors or chain switches. Its gateway now
retains only active tuples from the first ordinal-name actor with an active
capability instead of combining maximum rating and any active flag across actors.
Every legacy chain therefore shares one observed actor; this can underuse another
character's capabilities. The planner also rejects inactive tuples. Existing
schema and snapshot signatures remain intact; refresh development snapshots to
replace historical aggregate summaries. No durable migration/retention or live
protected-stock integration is introduced. No French presentation/text/layout
changes are made; the correction removes false capability eligibility.

Independent expected policy vectors live in `AccountEvidencePolicyTests`; gateway
vectors in `AccountHoldingsProtectionGatewayTests` extend the collector harness.
They cover armory/inactive/unknown locations, attached components and duplicate
copies, partial actor coverage, account-wide reserves, scope/binding failures,
actor rename/deletion, same-input replay, real actor chains/access and a four-read
cancellation barrier during crafting. The legacy rating/active regression failed
before the correction; it requires active rating 100 when another actor has
inactive 500. VERIFY-008/013/016/017 remain OPEN. The legacy eligibility correction changes rendered state through the existing
crafting consumer. [P02B actual 1920×1080 evidence](../ux/evidence/TKT-M22-P02B/README.md)
covers rejected mixed/inactive tuples and a retained eligible actor, with the
original 01/02 comparison, keyboard/axe/zoom checks and explicit inherited shell
limitations. Frontend source and fixture Signaux/Plans previews remain untouched.

## P02C production integration

The existing crafting account refresh and decision loop now call
`AccountHoldingsSnapshotService`, under the captured P03A account/store fence.
There is no second timer or scheduler. A successful identity read publishes all
typed source results atomically into `account_holdings_snapshots`; individual
failures keep their availability/error/coverage and previous rows only as stale
non-admission evidence. Equipment protection floors persist monotonically until
the authorized account-data clear. Restart/restore evidence remains non-admissible
until refreshed in the current generation. Integrity and restore validate the
normalized document, relational owner and capture time.

Public item metadata's documented `type` is mapped through the existing typed
gateway into explicit commodity/equipment/unknown categories. Unrecognized,
missing or failed category evidence is unknown, never disposable. This is a local
conservative classification, not proof of instance identity. The indexed official
[items contract](https://wiki.guildwars2.com/wiki/API:2/items) was inspected on
2026-10-01; direct wiki fetch returned 403. No authenticated probe or additional
key grant is claimed.

One projector exposes observed, protected/unknown, omitted, actor-usable and
tradeable quantities separately. While VERIFY-017 is OPEN it admits at most the
largest qualifying independent location per item (source/actor/bag/slot ties),
then applies keep quantity once. Portfolio lots/history are accounting provenance,
not another physical quantity; delivery contributes no stock or gold. Positive
local inventory/cash effects remain provisional accounting and are excluded from
admission; negative residual effects and global reservations constrain resources.
Moves create no acquisition, disposal or profit event and assign no cost basis.

Crafting evaluates each actual actor independently with this same Owned cap and
existing opportunity-cost/unknown economics. The supported slice requires one
capable active actor for the recipe chain, accessible bank/material/shared/own
inventory and available unlocks. Required other-character access or multi-actor
chains return `UnsupportedPrerequisite`. Plan documents retain recipe/actor and
selected location/binding commitments. Start, resume and consuming completion
revalidate latest SQLite evidence, account-wide residuals and reservations in the
same private lease/transaction; changed sources pause instead of rebinding.
Provisional Buy/Craft output alone cannot advance a consuming instruction.

Live physical frames always remain Partial, carrying independent per-source
clocks/coverage and a conservative minimum clock, never maximum fetch or guessed
upstream observation time. TP refresh/replay cannot create negative physical proof.
French response text exposes the required actor/access and pause; raw captures,
opaque actor IDs, account scope and store generations stay private. Preview
providers remain unchanged. [Actual 1920×1080 evidence and reproduction](../ux/evidence/TKT-M22-P02C/README.md)
cover eligible bank/own-bag/independent partial-source craft, protected/inaccessible
rejection and moved-input pause in the existing application.

Verification includes the pre-fix public-boundary 10+10 duplication failure,
conservative 6+4/150-minus-100/delivery vectors, actor/access/resume and provisional
output tests, real SQLite restart/partial-source/rollback/recovery and atomic
reservation/completion races. VERIFY-008/013/016/017 remain OPEN. This integration
does not certify coherent physical reconciliation, transfer/craft basis or the
later P02-E/P02D lifecycle exits.
