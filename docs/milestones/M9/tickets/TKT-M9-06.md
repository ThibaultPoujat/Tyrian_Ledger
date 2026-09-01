# TKT-M9-06 - Liquidity-safe finalist selection

## Goal

Make beginner fast-flip recommendations resistant to sparse, extreme
order-book outliers while retaining M9's keyless, player-triggered,
history-free workflow.

## Dependencies

- TKT-M9-05.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- Existing player scan lifecycle and beginner recommendation contracts.

## Acceptance criteria

- Aggregate finalist screening requires at least 10 buy units and 10 sell
  units, and accepts a planned sale price no more than twice the planned buy
  price using checked integer copper.
- The bounded detailed shortlist prioritizes the smaller aggregate side
  quantity, then raw price gap and item ID.
- Detailed candidates require at least 3 listings and 10 units on both buy and
  sell sides before modeled-profit, ROI, and recommendation ranking apply.
- Recommendations disclose the fixed current-order-book depth and
  relative-spread guard without guaranteeing a fill, sale, or profit.
- Settings presents the exact fixed cap, modeled ROI, and modeled profit floor
  for Cautious, Balanced, and Adventurous profiles.
- No new external endpoint, credential, cache, persistence, browser storage,
  history, or scan lifecycle behavior is introduced.

## Required tests

- Aggregate depth and planned-price-spread boundaries, including deterministic
  shortlist ordering and the 200-finalist bound.
- Detailed singleton/insufficient-depth rejection and assumption serialization.
- Settings profile-table and card-assumption coverage in frontend and browser
  tests.

## Non-goals

- Completed-sale history, price volatility, fill-time prediction, or a
  guarantee of market liquidity.
- User-configured liquidity thresholds, account data, Trading Post automation,
  or changes to fees, risk profiles, quantity rules, or final recommendation
  ranking.
