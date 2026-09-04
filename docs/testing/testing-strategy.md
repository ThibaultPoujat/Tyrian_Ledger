# Testing Strategy

## Purpose

Tests protect financial truth, local private data, API boundaries, persistence,
and the user-visible decision workflow. The M10-M11 static snapshot browser is
historical; active M12+ tests evolve toward the loopback ASP.NET + React + SQLite
runtime.

Normal automated tests use mocks/fixtures and must not require a live ArenaNet
key or mutate Guild Wars 2 state.

## Quality layers

### Domain and analytics unit tests

Required for deterministic business logic, including as introduced:

- integer-copper money/overflow behavior;
- canonical fee policy and rounding;
- completed-sale profit/ROI/max-bid calculations;
- order-book execution, depth, price impact and liquidity evidence;
- FIFO lot matching/partial fills;
- realized versus unrealized accounting;
- historical statistics/sufficiency;
- opportunity-score component behavior/anomaly flags;
- bankroll/position sizing/cash reserve/concentration;
- recommendation action-state composition;
- personal turnover weighting;
- owned-material/crafting economics and bounded path search.

High-risk formulas need independent expected vectors/edge cases, not tests that
merely restate the implementation expression.

### Gateway tests

Use synthetic recorded fixtures or mocked HTTP for:

- public prices/listings/item metadata;
- authenticated current/completed personal TP endpoints;
- token/permission status;
- later inventory/material/crafting endpoints;
- 401/403/404/429/5xx and transport failure;
- malformed/partial/new-field payloads;
- cancellation/retry/backoff/batching/request-budget behavior;
- secret non-disclosure in errors/logs/results.

Live API shape checks may exist as controlled manually triggered/release-stage
verification only and must never use committed credentials.

### Persistence/migration tests

Cover:

- fresh database initialization;
- migration upgrade paths;
- uniqueness/account scope;
- transaction-safe writes;
- idempotent repeated sync;
- partial remote failure preserving last-known-good data;
- remote history aging without local history loss;
- FIFO rebuild determinism;
- backup/restore/invalid-restore/clear behavior;
- market-history retention/downsampling integrity.

### Local-host integration tests

Cover:

- loopback default binding;
- startup without API key where supported;
- health/status endpoint;
- safe key-status/result payloads;
- no secret in browser-facing responses;
- React/local-API integration contract;
- restart persistence and background-service cancellation.

### Frontend/component tests

Cover accessible rendering/interactions for:

- account/sync/error/unknown states;
- personal dashboard/current orders;
- scanner/filter/detail/watchlist;
- historical coverage/confidence;
- `What Should I Do?` actions/reasons;
- investment/crafting/alert views as introduced.

Frontend tests should assert that browser code consumes backend financial
results rather than owning a competing formula.

### Browser E2E tests

Playwright should focus on high-value user journeys rather than every branch:

1. start/connect/status flow;
2. sync -> dashboard/current orders;
3. scanner -> detail/watchlist;
4. primary recommendation/action review;
5. restart with persisted state;
6. backup/restore representative flow;
7. keyboard navigation/focus/accessibility regression.

The browser **is allowed and expected to call the loopback local API**. It must
not call ArenaNet directly or receive the API key.

## Fixture policy

Fixtures must be deterministic, synthetic or sanitized, small enough to inspect,
and versioned with the behavior they support. Never copy a real credential or
raw private user payload into fixtures.

Golden vectors are useful for complex financial/statistical behavior when the
expected result was derived independently and reviewed. Avoid browser/server
duplicate implementations merely to generate matching golden values.

## Specification-test coupling

Every behavior ticket updates or validates at least one of:

- unit tests;
- integration tests/fixtures;
- persistence/migration tests;
- component/browser tests;
- specification/VERIFY text.

The ticket acceptance criteria define the required minimum. Tests must cover the
dangerous boundary around the change, not only the success path.

## Financial/data correctness gates

A PR must not merge with failing relevant tests for:

- copper/fee/ROI/max-bid math;
- order-book simulation;
- FIFO/accounting;
- migrations/sync/backup data integrity;
- historical statistics;
- score/risk/recommendation composition;
- crafting economics.

R3 tickets receive fresh flagship XHigh review in addition to tests.

## No runtime LLM tests

No application LLM exists in scope. Coding-agent quality is governed by ticket
contracts, repository tests, independent PR review, and functional owner review.
