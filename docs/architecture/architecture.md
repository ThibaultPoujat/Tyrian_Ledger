# Architecture

## Target stack

Recommended baseline:

- .NET 10 LTS backend with ASP.NET Core
- React + TypeScript frontend
- SQLite via EF Core or a narrowly-scoped persistence abstraction
- xUnit for .NET tests
- Playwright for browser smoke/end-to-end tests
- built-in structured logging with redaction rules

Alternative: Blazor Web App if a single-language C# stack is strongly preferred. Do not switch to Blazor solely because it looks simpler on day one; the current specification benefits from a clear browser/client boundary and TypeScript ecosystem.

## Runtime topology

Browser -> ASP.NET Core local server -> application services -> deterministic analytics / SQLite / GW2 API gateway

No browser component uses credentials or calls authenticated GW2 endpoints.

## Layers

### Domain

Pure models and invariants. No HTTP, database, framework, or UI dependencies.

### Application

Use cases and orchestration. Defines public-market data and recommendation interfaces, plus a clock where required.

### Infrastructure

GW2 HTTP implementation, caching, persistence, secret handling, filesystem configuration, external adapters.

### Analytics

Pure deterministic calculators: integer-copper fees, market scenarios, order-book simulation, and M9 recommendation rules.

### Web

HTTP endpoints, DTO mapping, UI, validation, presentation.

## Recommended repository structure

```text
/
  src/
    Gw2Tp.Web/
    Gw2Tp.Application/
    Gw2Tp.Domain/
    Gw2Tp.Analytics/
    Gw2Tp.Infrastructure/
  tests/
    Gw2Tp.Domain.Tests/
    Gw2Tp.Application.Tests/
    Gw2Tp.Analytics.Tests/
    Gw2Tp.Infrastructure.Tests/
    Gw2Tp.IntegrationTests/
    Gw2Tp.Web.E2E/
  docs/
```

## External API gateway

All GW2 API calls MUST pass through one abstraction:

`IGw2ApiClient`

with internal components:

- request scheduler;
- cache;
- deduplicator;
- batching coordinator;
- retry/backoff policy;
- response validation;
- metrics/logging.

Feature code must never construct an ArenaNet URL itself.

## API caching

Reference data: long TTL and explicit refresh/invalidation.

Market data: short TTL and freshness metadata.

M9 market reads are current-scan inputs only. Cached public responses are transient process state and are never persisted as historical snapshots.

## Rate-limit strategy

The request scheduler should support a configurable token bucket. Current documented/community limits must be verified before release, not hard-coded as immutable facts.

On 429:

1. record the event;
2. respect server-provided retry information when available;
3. apply bounded exponential backoff;
4. suppress duplicate concurrent requests;
5. surface a useful UI state.

## Persistence

SQLite is the default local store. Use migrations and schema versioning.

Persist only local settings required for the active feature. M9 does not retain market snapshots, recommendation results, partial scans, account data, or personal history.

## Money

Represent money in integer copper using a dedicated value type or equivalent. No floating-point arithmetic for gold/silver/copper calculations.

Transaction-fee policy is provided by the caller as independent listing and
exchange rules, each expressed in basis points with explicit whole-copper
rounding. The application does not embed a default Guild Wars 2 fee schedule.

A flip-profit scenario models a completed sale for total transaction values:

`net sale proceeds = gross sale value - listing fee - exchange fee`

`net profit = net sale proceeds - acquisition cost`

The listing fee is included as an up-front cost in this completed-sale model.
Unsold or cancelled listings are not represented by this scenario.

## Order-book simulation

Order-book calculations model immediate execution against a supplied snapshot;
they are scenarios, not guarantees of a real-world fill. Acquisition consumes
sell levels from the lowest unit price upward, and liquidation consumes buy
levels from the highest unit price downward, regardless of source ordering.

The weighted average unit price remains exact as total copper divided by the
filled quantity; no floating-point or whole-copper rounding is applied. Price
impact is a total-copper comparison against filling the executed quantity at
the best available level: actual acquisition cost less the best-ask baseline,
or best-bid baseline less actual liquidation proceeds. Insufficient depth
returns the partial execution, its remaining quantity, and an explicit
incomplete status.

## Credentials

M9 does not read, store, or require Guild Wars 2 API credentials. The browser
receives only public-market data from the local server.

## Local network binding

Default server address: `127.0.0.1`.

Do not expose the application to the LAN by default.

## Error taxonomy

External failures should map to stable application error categories:

- RateLimited
- TemporarilyUnavailable
- NotFound
- InvalidRemoteData
- UnsupportedSchema
- LocalConfigurationError

## Observability

Track:

- API request count by endpoint category;
- cache hit/miss;
- request latency;
- 429 count;
- failed parsing;
- market data age;
- analytics duration;
- number of candidates analyzed;
- search truncation events.

Never log credentials, authorization headers, or private account payloads.

## LLM separation

No application LLM in current scope. The coding model is an external development dependency. If a future LLM feature is added, it must consume structured application output through an adapter and must not access secrets or mutate state.
