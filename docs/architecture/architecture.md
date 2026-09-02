# Architecture

## Target stack

Recommended baseline:

- .NET 10 LTS snapshot generator and deterministic calculation libraries
- React + TypeScript static frontend
- versioned public market snapshot artifact
- xUnit for .NET tests
- Playwright for browser smoke/end-to-end tests
- built-in structured logging with redaction rules

No server-rendered or dynamic web framework is part of the static delivery.

## Runtime topology

Scheduled generator -> application services -> deterministic analytics / typed GW2 API gateway -> market-snapshot.json

Browser -> static React assets plus market-snapshot.json

No browser component uses credentials, calls Guild Wars 2, or calls a local API.

## Layers

### Domain

Pure models and invariants. No HTTP, database, framework, or UI dependencies.

### Application

Use cases and orchestration. Defines public-market data and recommendation interfaces, plus a clock where required.

### Infrastructure

GW2 HTTP implementation, transient capture caching, scheduler, filesystem output,
and external adapters used by the generator.

### Analytics

Pure deterministic calculators: integer-copper fees, market scenarios, order-book simulation, and M9 recommendation rules.

## Recommended repository structure

```text
/
  src/
    Gw2Tp.Application/
    Gw2Tp.Domain/
    Gw2Tp.Analytics/
    Gw2Tp.Infrastructure/
    Gw2Tp.MarketSnapshotGenerator/
  tests/
    Gw2Tp.Domain.Tests/
    Gw2Tp.Application.Tests/
    Gw2Tp.Analytics.Tests/
    Gw2Tp.Infrastructure.Tests/
    Gw2Tp.MarketSnapshotGenerator.Tests/
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

Market reads are scheduled-capture inputs only. Cached public responses are
transient generator process state and are never persisted as historical
snapshots.

## Rate-limit strategy

The request scheduler should support a configurable token bucket. Current documented/community limits must be verified before release, not hard-coded as immutable facts.

On 429:

1. record the event;
2. respect server-provided retry information when available;
3. apply bounded exponential backoff;
4. suppress duplicate concurrent requests;
5. surface a useful UI state.

## Persistence

The static site has no server-side database or persistence layer. Capital and
risk settings are browser-local; generated market data is the current
publishable snapshot artifact rather than retained application history.

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

The browser does not read, store, or require Guild Wars 2 API credentials. It
receives only public-market data from the published static snapshot.

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
