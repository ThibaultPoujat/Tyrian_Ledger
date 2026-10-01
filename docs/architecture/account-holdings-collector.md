# Account Holdings Collector — P02A Handoff

P02A / #168 adds a disconnected producer for B2. It does not persist, poll,
reserve, value or admit resources, and has no browser endpoint. P02B extends
protection/actor evidence; P03A guards account/store publication before P02C
integrates the collector. The existing crafting snapshot/gateway remains unchanged.

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
Production composition does not call this extension. Tests can inject the typed
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
