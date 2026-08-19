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
- Opportunity scoring
- Session planning
- Owned-item opportunity cost
- Crafting graph/search
- Data validation and normalization

### Integration tests

Use recorded fixtures or mocks for GW2 responses. Normal automated tests must not call live endpoints.

Cover:

- prices
- listings
- recipes
- account tokeninfo
- bank/materials/inventory
- character crafting/recipe availability
- transaction history where used
- 401/403/404/429/5xx
- malformed JSON
- missing/new fields

### Contract/smoke tests

A controlled, manually triggered or release-stage suite MAY call the live API to confirm endpoint shape. This is not the normal CI suite.

### Browser tests

Use Playwright to test the highest-value journeys:

1. dashboard loads with fixture data;
2. filters work;
3. opportunity detail explains assumptions;
4. API permission failure is understandable;
5. no secret appears in HTML or network responses;
6. data freshness is visible.

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
- crafting economic cost;
- realized profit reconciliation.

## LLM testing

No LLM application tests exist in current scope. Development-agent behavior is evaluated operationally through ticket acceptance and repository tests.

If a future LLM feature is added, deterministic invariants must still be tested outside the LLM.
