# TKT-M9-03 - Deterministic beginner recommendations

## Goal

Create deterministic domain/application logic that turns a complete current
market input into transparent beginner fast-flip recommendations.

## Dependencies

- TKT-M9-02.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- Existing money, fee, order-book, profitability, liquidity, and scoring
  contracts that remain applicable after TKT-M9-01.

## Acceptance criteria

- The only selectable risk profiles are Cautious, Balanced, and Adventurous,
  with the approved spend caps, ROI thresholds, and profit floors from M9.
- Given capital, risk profile, current prices/listings, and item stack limit,
  the engine chooses the largest whole quantity constrained by the profile
  spend cap and one normal stack.
- Buy price is one copper above the current best buyer and sale price is one
  copper below the current cheapest seller. The engine rejects invalid,
  non-positive, or unavailable prices.
- Every money calculation uses integer copper. Gross sale, built-in listing
  fee, exchange fee, total cost, modeled profit, and modeled ROI are
  deterministic and exposed for explanation.
- The built-in fee policy is 5% listing plus 10% exchange. Implement exact
  rounding/minimum behavior only after authoritative verification; update
  VERIFY-013 with evidence and do not invent a rule.
- The engine identifies the immediate and buy-order-and-wait routes from
  current order-book evidence without guaranteeing fill time, sales, or profit.
- Ranking is deterministic, explicit, stable for equal inputs, and returns no
  more than five independent recommendations. Recommendations disclose the
  scan time and assumptions needed for the UI.

## Required tests

- Table-driven integer-copper tests for each risk profile, spend boundary,
  stack boundary, and minimum profit/ROI boundary.
- Fee boundary tests for verified rounding/minimum behavior.
- Price-underbid/overbid, invalid input, zero quantity, depth-route, and
  deterministic tie-break tests.
- Tests proving no floating-point money path, persistence, account input, or
  historical data is required.

## Non-goals

- HTTP fetch scheduling, progress, cancellation, or error presentation from
  TKT-M9-04.
- Browser pages, onboarding, settings forms, or visual card design from
  TKT-M9-05.
- Long-horizon strategies, portfolio allocation, or post-trade tracking.
