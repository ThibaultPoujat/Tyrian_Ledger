# Architecture - Personal Local-First Runtime

> Target product/UX and Windows lifecycle clarification: [ADR-011](../adr/ADR-011-approved-session-assistant-and-visual-authority.md). The tray companion and corrective evidence/read-model work are planned requirements, not already delivered capabilities. Follow the approved package gates; this does not authorize a stack rewrite.

## 1. Target stack

- .NET 10 / ASP.NET Core local host
- existing C# Domain, Application, Analytics, and Infrastructure libraries
- React + TypeScript frontend
- SQLite local persistence
- xUnit for .NET tests
- frontend unit/component tests
- Playwright for browser end-to-end coverage
- built-in structured logging with secret/account-data redaction rules

The M10-M11 static GitHub Pages topology is superseded by ADR-010 and is
historical architecture; it is not an active product runtime.

## 2. Runtime topology

```text
Browser / React
      |
      | loopback HTTP only by default
      v
ASP.NET Core local host
      |
      +--> Application orchestration
      |       |
      |       +--> deterministic Analytics / Domain
      |       +--> persistence abstractions
      |       +--> ArenaNet gateway abstractions
      |
      +--> Infrastructure
              +--> SQLite
              +--> OS secret store
              +--> typed ArenaNet HTTP clients
              +--> request scheduler/cache/batching/retry
              +--> background market-history collector
```

No public hosted API or cloud database is required for V1.

## 3. Layer responsibilities

### Domain

Pure value types, invariants, and domain state. No HTTP, SQLite, ASP.NET, React,
or external DTO dependencies.

Money remains an exact integer-copper value. Domain types should prefer explicit
unknown/unsupported states over sentinel values.

### Analytics

Pure deterministic calculators such as:

- Trading Post fees and completed-sale scenarios;
- order-book execution/depth/price-impact simulation;
- FIFO lot matching and realized/unrealized calculations where the dependency
  direction remains clean;
- historical statistics;
- opportunity score components;
- position/risk sizing.

Analytics must be reproducible from explicit inputs and straightforward to unit
test.

### Application

Use cases and orchestration:

- account/key status;
- personal TP synchronization;
- dashboard queries;
- scanner orchestration;
- historical collection policy;
- recommendation orchestration;
- backup/restore commands;
- later investment/crafting workflows.

Application defines interfaces for infrastructure concerns and stable result/error
contracts for the local host.

Plan reconciliation accepts one typed, account-scoped evidence frame with
independent provenance for physical inventory, coin, current Trading Post orders,
and completed transactions. Each source keeps its capture identity and local
fetch time separate from any upstream observation time; unavailable timestamps
remain unknown. The loopback host maps source facts into this frame, while the
Application layer owns positive matching, evidence consumption, completeness
gates, and contradiction progress. A frame for another account is ignored.

Current account producers expose wallet and bank/material snapshots but not all
character inventories, so the physical-inventory source is partial and cannot
confirm or contradict craft deltas. A successful atomic Trading Post sync may
mark its current-order and completed-transaction sources complete under one
shared local capture identity. This says the required endpoints were read as one
sync; it makes no guarantee that ArenaNet served newly observed data. Craft
confirmation requires a complete physical frame covering every affected item
and an upstream observation at or after the reported action. A listing or a
Trading Post capture alone cannot prove ingredient consumption. Unknown or
partial evidence keeps the shadow provisional. Carry VERIFY-008 forward for
upstream cache freshness uncertainty.

When repeated, source-qualified evidence contradicts a local execution event,
the plan retains a typed reconciliation reason code with its paused state. The
API exposes the stable code; French explanation belongs at the presentation
boundary and never changes reconciliation policy.

### Infrastructure

External adapters:

- ArenaNet HTTP DTOs/clients;
- authentication-header injection;
- OS-backed secret storage;
- SQLite repositories/migrations;
- filesystem backup/restore;
- clocks/schedulers;
- structured logging/metrics.

External DTOs never leak directly to Domain or React.

### Local host

ASP.NET Core provides thin local endpoints and hosts/coordinates background
services. Endpoints validate transport-level input, invoke Application use
cases, and return structured view/query models. Financial formulas do not live
in controllers/minimal API handlers.

The host binds loopback by default and rejects wildcard/LAN/Internet listeners
in normal configuration. It validates `Host` against an explicit allowlist (for
example ASP.NET Core `AllowedHosts`) so loopback binding is not treated as
sufficient DNS-rebinding protection. LAN/Internet exposure requires a future
owner-approved security/architecture decision.

Production serves React and the API from one origin. A separate development
server may use CORS only for exact configured trusted development origins;
wildcard or reflected origins are forbidden. Every state-changing local
endpoint also requires an explicit cross-origin request defense, such as
validated same-origin metadata plus an anti-forgery token/custom-header policy.
CORS is not CSRF protection.

The concrete host is `src/Gw2Tp.Web/Gw2Tp.Web.csproj`. It references
Application and Infrastructure, exposes the structured `GET /api/health`
transport contract, and contains only host/bootstrap security policy at this
stage. `tests/Gw2Tp.Web.Tests` exercises its startup and browser-facing network
boundary.

Normal startup reads `TyrianLedger:Host`, validates explicit IPv4/IPv6 loopback
listen addresses, and configures Kestrel directly. Generic URL, HTTP/HTTPS port,
and `Kestrel:Endpoints` overrides are rejected to prevent configuration from
bypassing that validation. The default reloadable Kestrel endpoint loader is
replaced with an empty, non-reloadable source so a later configuration reload
cannot add a listener outside the validated policy. Host filtering uses the
separately explicit `AllowedHosts` list and rejects ASP.NET Core's wildcard host
aliases. Production enables no CORS policy; Development permits only the exact
`TrustedDevelopmentOrigins` entries.

Unsafe HTTP methods pass through origin protection before endpoint execution.
They require an exact same-origin `Origin`, with exact configured development
origins additionally accepted only in Development, plus the explicit
`X-Tyrian-Ledger-Request: 1` application-request header. The
credential-dependent account-connection GET requires the same header and
rejects an untrusted supplied Origin, so a cross-origin page cannot repeatedly
trigger vault access or authenticated validation. This control is independent
of CORS and applies before any future state-changing endpoint is introduced.

Vite proxies relative `/api` calls to the loopback host for development. A
Release publish builds React into the host output; the production host serves
those assets plus the API from one origin and falls back to `index.html` for
client routes. Operational commands and configuration are documented in
`docs/development/local-runtime.md`.

### React frontend

React owns presentation, user input, navigation, filtering/sorting of already
structured safe results where doing so cannot change financial truth, and
accessible interaction states.

React must not:

- receive/store the ArenaNet API key;
- construct ArenaNet requests;
- own canonical fee/profit/cost-basis/recommendation formulas;
- treat browser localStorage as the authoritative financial database.

## 4. ArenaNet gateway

All Guild Wars 2 calls pass through typed abstractions. Public-market and
personal/authenticated operations may be split into narrower interfaces as the
surface grows, but they share common infrastructure policy where appropriate:

- bounded concurrency/request budget;
- batching;
- cache/deduplication;
- retry/backoff;
- `Retry-After` handling;
- response validation;
- stable error taxonomy;
- metrics/logging with secret redaction.

Feature code never constructs ArenaNet URLs.

Authenticated personal requests obtain credentials from the host/infrastructure
secret provider. The key is applied at the HTTP boundary and never included in
application result objects.

## 5. Persistence

SQLite is the durable source for locally owned data. Initial areas include:

- local account profile/scope;
- completed personal TP transactions;
- current personal TP order snapshots;
- user settings/watchlists;
- schema/version metadata;
- later market-history observations, lots/matches, positions, recommendation
  snapshots, and personal-performance observations.

See `docs/architecture/data-model.md`.

Persistence rules:

- UTC timestamps;
- integer-copper prices;
- uniqueness constraints for authoritative external IDs;
- migration tests;
- transaction-safe writes;
- incomplete sync must not wipe prior good data;
- no API key in SQLite;
- destructive migration/retention behavior requires explicit owner approval.

Derived data should be rebuildable or versioned from authoritative inputs.

## 6. Personal TP synchronization

Sync must be idempotent.

Completed transaction history is append/upsert by authoritative external ID.
Current order state is updated only after a successful complete read of the
relevant endpoint set. An order disappearing from a current-order response is
not automatically a completed trade; completion requires appropriate history
evidence.

Locally imported completed history is retained after it falls outside the
remote API's accessible history window.

## 7. Market-history collection

The local host eventually owns a background scheduler using the existing ArenaNet
request scheduler/gateway.

Sampling tiers:

1. current personal orders, plus held positions after TKT-M20-03 introduces and
   registers that source;
2. watchlist/approved markets;
3. broader tradable universe;
4. detailed full order books only for shortlisted/high-interest items.

Best-price snapshots are much cheaper to retain than complete books. Collection
policy, retention, and storage growth are explicit and configurable.

A failed/partial capture never overwrites or fabricates an observation.

## 8. Financial/recommendation boundary

The backend produces authoritative structures containing calculations,
components, explanations, and confidence. React displays them.

Recommendations combine independent tested components rather than embedding one
large untestable controller/service. Examples:

- fee/profit scenario;
- current depth/liquidity evidence;
- historical statistics;
- personal performance evidence;
- risk/position sizing;
- action-state orchestration.

This allows a reviewer to verify each layer separately.

## 9. Secrets and privacy

ADR-006 remains active. Supported OS-backed secret storage is preferred; an
environment variable is development/test fallback only. No plaintext-file
fallback is allowed merely for convenience.

Logs must never contain:

- API keys/authorization headers;
- raw private account payloads unless an explicitly sanitized debug fixture is
  created outside production data;
- secrets from environment or OS secret stores.

Browser network payloads contain only the minimum safe account/status/result
information required by the UI.

## 10. Error taxonomy

Use stable application-level categories such as:

- `RateLimited`
- `TemporarilyUnavailable`
- `NotFound`
- `InvalidRemoteData`
- `IncompleteData`
- `MissingPermission`
- `UnsupportedSchema`
- `LocalConfigurationError`
- `PersistenceFailure`

Do not surface raw secret-bearing upstream exceptions to the browser.

## 11. Observability

Useful local metrics/logs include:

- API requests by endpoint category;
- cache hit/miss;
- latency and 429 count;
- parse/validation failures;
- personal sync age/coverage;
- market-history last success and tracked count;
- scanner candidate counts/truncation;
- analytics duration;
- database migration/backup status.

Observability stays privacy-minimized and local by default.

## 12. Transition from M10-M11

During M12:

- keep reusable C# financial/gateway/order-book code and tests;
- keep reusable React accessibility/UI pieces;
- retire Pages publication workflow/scripts/scheduler;
- retire static snapshot loading/publication contracts that exist only for the
  public site;
- retire duplicate browser authoritative recommendation calculations after
  equivalent server-side behavior is protected by tests;
- preserve old ADRs/docs as explicitly superseded history where useful.

Do not perform a broad rewrite just to match a new directory diagram.


## 13. Public reference reuse (P03B1)

Infrastructure owns one process-memory `PublicReferenceCache`, shared by the
batching `IGw2ApiClient` item-metadata method and `ICraftingReferenceGateway` recipe
definitions. Real holdings category refresh and crafting discovery use these
same seams. The current recipe gateway has no public recipe-ID universe method;
this ticket does not add one or cache private account recipe unlocks.

Keys include endpoint, pinned schema, language (`en` for items; language-neutral
recipes) and each normalized positive ID. Reordered/duplicate reference IDs share
data; overlapping calls fetch only missing IDs in existing sequential 200-ID
batches. Only complete validated successful fills enter the cache. Partial,
missing, duplicate, unexpected, malformed, failed and cancelled fills cannot
create complete cached evidence; existing valid hits survive a failed fill.

`Gw2Api:PublicReferences:TimeToLiveSeconds` (default 3600) and `MaximumEntries`
(default 10000) are positive, startup-validated local reuse settings, not ArenaNet
update/cache guarantees. Capacity counts references across both endpoints;
eviction is deterministic FIFO, without hit-based age extension. TTL starts
before the fill, expires at the exact boundary, and is checked again before
returning mixed hit/miss results. Backward clock movement clears entries and
invalidates earlier in-flight fills. No stale-while-revalidate admission exists.
The cache does not claim physical/account freshness or resolve VERIFY-008/017.

The scheduler still owns bounded requests, retries, in-flight coalescing and
per-waiter cancellation. Hits require no outbound permit. Recipes' nested lists
and result collections are detached so callers cannot corrupt later reads.
No credential, account scope/roster/unlock, wallet, holdings, TP or commerce market
response is retained here; account/store generation fences remain independent.
Clear/restore/account changes do not turn public references into private rights.
There is no persistence, new timer, permission or retention change.

Local aggregate counters under meter `TyrianLedger.PublicReferences` expose
`gw2.references.hits`, `gw2.references.misses` and `gw2.references.evictions` without
tags, IDs, URLs or secrets. A bounded scalar snapshot also reports retained entry
count; it adds no browser payload. Reproduce request-count and policy vectors with
`dotnet test tests/Gw2Tp.Infrastructure.Tests/Gw2Tp.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~PublicReferenceCacheTests|FullyQualifiedName~Real_holdings_and_crafting_consumers'`.
VERIFY-008/013/016/017 remain OPEN; synthetic tests are local policy evidence.

## 14. Request purpose, bounded dispatch and retries (P03B2)

`Gw2RequestPurposeScope` is a trusted Application context propagated through typed
gateways. It carries only `ActionValidation`, `AccountRefresh` or
`BackgroundResearch`; unclassified calls default to refresh. Neither HTTP query
parameters nor React select priority. Purpose does not change request identity,
account/resource authority or financial admission.

Production caller inventory:

| Caller / outbound boundary | Purpose |
|---|---|
| `PlanEndpointService.StartAsync`, `CompleteAsync`, `UndoAsync` | Action validation for account/order/holdings/selected listing reads; nested broad discovery lowers itself to research |
| `ContinuousDecisionLoopService` synchronization, holdings/crafting refresh and bounded decisions | Account refresh; nested scanner/history/crafting discovery remains research |
| `PublicMarketSnapshotCollector`, `LiveMarketScanner`, `MarketHistoryCollector` scheduled/manual collection | Background research for index, prices, finalist listings/metadata |
| `CraftingOpportunityService` account preparation, recipes, input/output listings/metadata | Background research; this discovers candidates rather than validating a committed action |
| `PersonalTradingPostSynchronizationService`, `AccountHoldingsSnapshotService`, `AccountCraftingSnapshotService` | Inherit action/refresh context; direct unclassified/manual calls default to refresh |
| `PersonalDashboardService`, `PrimaryRecommendationService` existing holdings/orders, `InvestmentPortfolioService` existing positions | Inherit trusted caller purpose; otherwise refresh; embedded new-market scanner explicitly lowers to research |
| Account connection/status, watchlist/investment/preferences/explanation supporting endpoints | Ordinary refresh for their scope/status or bounded evidence reads |
| Typed public item/recipe gateways | Inherit caller purpose on misses; P03B1 completed hits avoid dispatch |
| Private account/TP/crafting/holdings gateways | Inherit caller purpose; private keys include captured host generation, never credentials |

One `Gw2RequestScheduler` owns logical sharing, attempts, the existing token
bucket and concurrency. Its fair queue replaces the two FIFO limiter queues.
Running HTTP is not interrupted for priority. FIFO applies within each class;
a waiting background class gets a turn after at most eight higher-priority
dispatches. Refresh gets a turn after eight action dispatches when a background
turn is not due. Counters reset when their class stops waiting. A single rate
waiter obtains a token before choosing work, so low-priority work cannot reserve
the next token or any HTTP concurrency while waiting.

`Gw2Api:RateLimit:MaxQueuedRequests` bounds all pending logical work including
retry cooldown (default 100). `Gw2Api:Queue:ActionValidationLimit` and
`AccountRefreshLimit` default to 100 each; `BackgroundResearchLimit` defaults to
80, leaving capacity for foreground/refresh under a saturated research class.
The effective limit is the lower of total and class limits. Limits must be
nonnegative; zero disables waiting for that class/total, while immediately
available dispatch remains possible. Burst/refill/concurrency/timeout and retry
attempt validations remain positive. These settings are local policy, not
ArenaNet quota assertions.

Same-key waiters share one logical slot. A higher-purpose waiter promotes queued
work only when class capacity allows; a rejected promotion leaves existing work
intact. Promotion lasts for the shared logical operation even if that waiter
cancels. Last-waiter cancellation immediately removes waiting work and cancels
its HTTP/backoff. HTTP retains its active slot until it actually exits. Each
retry releases HTTP concurrency, reserves bounded pending capacity during its
private cooldown, then re-enters the same fair/rate queue. Capacity failure maps
through existing typed gateway degraded results. Server Retry-After (delta/date)
is honored by the typed readers; a wait above `Retry:MaxServerRetryAfterMs`
(positive, default 300000 milliseconds) returns the
original degraded 429 instead of retrying early or retaining an arbitrarily long
cooldown. No global server cooldown is inferred. Existing max-attempt, HTTP
timeout and 5xx retry classifications remain in force.

Private work captures the fence generation as part of scheduler identity.
Credential/account/clear/restore invalidation removes obsolete queued work and
rejects its waiters with `AccountWorkRejectedException`; private generations never
share work. Host stopping cancels queued work and retry delay. Captured
credentials, SQLite commit leases and late-publication checks remain independent
required authorities. Public references do not confer private freshness.

Safe metrics contain aggregate queue depth, wait duration and dispatch counts,
without endpoint/ID/account/actor/key tags or payloads. Internal diagnostic
snapshots expose bounded pending/active/peak depths and counters. No new timer,
browser payload, completed private cache, research universe, financial fallback,
UI or production dependency is added. P03C1 still owns removing network work from
local completion/undo; scheduling priority does not satisfy that outcome.

[Actual bounded local transport trace](../verification/evidence/TKT-M22-P03B2/load-trace.json)
records two overlapping synthetic 30-item scans and eight action reads separately
from barrier/controlled-delay policy tests. It proves observed local dispatch,
sharing and bounds only; it is not an authenticated ArenaNet, Windows or latency
measurement. Reproduce with:

```sh
TYRIAN_LEDGER_P03B2_LOAD_TRACE="$PWD/docs/verification/evidence/TKT-M22-P03B2/load-trace.json" dotnet test tests/Gw2Tp.Infrastructure.Tests/Gw2Tp.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~Actual_bounded_local_HTTP_load_trace --verbosity quiet
```

VERIFY-008/013/016/017 remain OPEN. No upstream permission, fee, cache/coherence or
physical-correlation fact is resolved by these policy tests.
