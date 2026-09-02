# Testing Strategy

## Quality layers

### Unit tests

Required for all deterministic business logic:

- Money and currency conversion
- Fee policy
- Profit calculations
- ROI and capital efficiency
- Order-book depth simulation
- Liquidity metrics
- Freshness classification
- M9 recommendation eligibility and ranking
- Data validation and normalization

### Gateway and generator tests

Use recorded fixtures or mocks for GW2 responses. Normal automated tests must not call live endpoints.

Cover:

- public prices, listings, and item metadata
- 401/403/404/429/5xx
- malformed JSON
- missing/new fields
- bounded snapshot collection, contract validation, and atomic generator output

### Contract/smoke tests

A controlled, manually triggered or release-stage suite MAY call the live API to confirm endpoint shape. This is not the normal CI suite.

### Browser tests

Use Playwright to test the highest-value journeys:

1. Recommendations and Settings are the only active destinations;
2. static snapshot loading covers fresh, delayed, malformed, and unavailable states;
3. browser traffic never reaches `/api` or Guild Wars 2;
4. keyboard operation and accessibility remain covered.

## Test fixture policy

Fixtures must be:

- deterministic;
- synthetic or sanitized;
- small enough to inspect manually;
- versioned with the test that depends on them.

## Specification-test coupling

Every ticket that changes behavior must update at least one of:

- acceptance tests;
- unit tests;
- integration fixtures/tests;
- browser tests;
- specification text.

The PR/commit should make the change relationship obvious.

## Financial correctness gates

The following cannot ship with failing tests:

- copper arithmetic;
- fee calculation;
- order-book simulation;
- M9 recommendation calculation and ranking.

## LLM testing

No LLM application tests exist in current scope. Development-agent behavior is evaluated operationally through ticket acceptance and repository tests.

If a future LLM feature is added, deterministic invariants must still be tested outside the LLM.
