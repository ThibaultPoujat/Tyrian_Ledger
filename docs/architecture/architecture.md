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

No browser component calls GW2 authenticated endpoints directly.

## Layers

### Domain

Pure models and invariants. No HTTP, database, framework, or UI dependencies.

### Application

Use cases and orchestration. Defines interfaces such as market data provider, account data provider, snapshot repository, clock, and secret store.

### Infrastructure

GW2 HTTP implementation, caching, persistence, secret handling, filesystem configuration, external adapters.

### Analytics

Pure deterministic calculators: fees, market scenarios, order-book simulation, crafting path search, opportunity score, stale-data handling.

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

Account snapshots: user-controlled refresh or moderate TTL.

All cached authenticated responses are scoped to a local account identity/key identifier and are never mixed across account contexts.

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

Persist only information needed for the feature. Keep raw API snapshots only where they provide a clear debugging/historical value.

## Money

Represent money in integer copper using a dedicated value type or equivalent. No floating-point arithmetic for gold/silver/copper calculations.

## Secrets

Use an OS-backed local secret store through an abstraction. For development, support an environment-variable override that is explicitly documented as local development only.

## Local network binding

Default server address: `127.0.0.1`.

Do not expose the application to the LAN by default.

## Error taxonomy

External failures should map to stable application error categories:

- AuthenticationFailed
- PermissionDenied
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

Never log API keys, authorization headers, or complete sensitive account payloads.

## LLM separation

No application LLM in current scope. The coding model is an external development dependency. If a future LLM feature is added, it must consume structured application output through an adapter and must not access secrets or mutate state.
