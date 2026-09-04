# Architecture - Personal Local-First Runtime

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
transition code to retire during M12.

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

1. current personal orders and held positions;
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
