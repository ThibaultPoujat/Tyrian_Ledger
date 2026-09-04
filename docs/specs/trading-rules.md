# Trading Rules and Recommendation Policy

This document defines the behavioral rules that financially authoritative code
must implement. VERIFY-013 is open: the current GW2 model uses separate 5%
listing and 10% exchange fees, a 1-copper minimum for each, and owner-approved
per-fee round-up behavior. The rates/minimums have documented wiki support, but
fractional-copper rounding remains externally unverified. Therefore all current
fee-derived results are **modeled/provisional**, not verified external behavior,
until TKT-M15-01 records sufficient evidence and resolves VERIFY-013.

## 1. Money and precision

- Authoritative monetary values use integer copper.
- Never use binary floating point for purchase cost, sale value, fees, profit,
  cost basis, max bid, or allocated capital.
- Statistical ratios may use an appropriate numeric representation, but input
  money stays exact and rounding semantics must be documented.
- Overflow and invalid negative values must fail safely rather than wrap.

## 2. Fee-aware economics

For a completed sale scenario:

`net sale proceeds = gross sale value - listing fee - exchange fee`

`net profit = net sale proceeds - acquisition cost`

Listing/exchange fee rates, minimums, and whole-copper rounding come from one
central application policy. Generic fee primitives remain reusable, but
feature/UI code must never duplicate fee constants. Tests must distinguish
correct implementation of the current provisional policy from external
verification of the real GW2 rounding contract.

Listing fees matter twice to behavior:

1. they reduce modeled profit;
2. cancellation/relisting can destroy already-paid listing value and queue
   position.

A recommendation that proposes relisting must consider that incremental cost.

## 3. ROI and absolute profit

A candidate must satisfy both a configurable minimum net ROI and minimum
absolute net profit when those filters are enabled. Very small profitable trades
may still be poor uses of attention/capital.

The exact ROI denominator must be centralized with the canonical calculation
policy. It should represent capital economically committed to the scenario and
remain consistent across scanner, history, recommendation, and UI.

## 4. Maximum bid rule

For a target sale/listing scenario and target minimum ROI, the **maximum allowed
bid** is the highest integer-copper acquisition price for which the canonical
fee-aware scenario still meets:

- minimum target ROI;
- minimum absolute profit if configured;
- applicable position/risk constraints.

Do not derive max bid with a browser-side shortcut. Because fee rounding is
piecewise in integer copper, authoritative code should solve/check the exact
integer condition.

A current order above max bid is economically invalid under the active policy.
A recommendation must never advise chasing another bidder beyond max bid.

## 5. Current spread is not enough

The scanner may use aggregate best prices for broad screening, but a shortlisted
candidate should use detailed order-book evidence when practical.

Evaluate evidence such as:

- quantity and listing count near best bid/ask;
- depth consumed by intended quantity;
- weighted/actual acquisition and liquidation values;
- price impact relative to the best visible level;
- support gaps/price cliffs behind the top level;
- imbalance or abrupt depth changes;
- freshness of the observation.

A one-unit best price must not make an otherwise empty market appear liquid.

## 6. Historical persistence and stability

Locally owned observations should eventually inform:

- median net ROI over available 7-day and 30-day windows;
- fraction of observations meeting configured ROI thresholds;
- price and spread volatility;
- liquidity/depth stability;
- observed ranges/drawdown where useful;
- exact sample count and coverage.

A requested historical window with insufficient coverage is `InsufficientData`,
not a shorter window mislabeled as 30 days.

Current extreme ROI relative to history is an anomaly signal, not automatically
a stronger opportunity.

## 7. Explainable opportunity score

Ranking combines named components rather than a single hidden formula. The
initial conceptual components are:

- expected exact net profit/ROI;
- current liquidity/depth quality;
- historical spread persistence;
- historical stability;
- personal fill/capital-turnover evidence when sufficiently sampled;
- anomaly and risk penalties.

Every score must expose component contributions or equivalent reasons. A stable,
liquid 20-30% market should be able to outrank a 100% headline ROI market with
near-zero depth.

No runtime LLM or opaque ML model may own this score.

## 8. Position sizing and bankroll protection

Suggested size is bounded by the minimum of independently explainable caps:

- deployable cash after reserve;
- single-market exposure cap;
- liquidity/order-book participation cap;
- existing open order/position exposure;
- strategy/category concentration cap;
- speculative/illiquid cap where applicable.

Initial configurable reference defaults may include approximately:

- 15% cash reserve;
- up to ~5% of bankroll for high-liquidity single-market exposure;
- ~2.5-3% for medium-liquidity exposure;
- ~1-2% for low-liquidity/speculative exposure.

These are starting policy values to validate through use; they are not hidden
hard-coded truths. Existing positions/orders count toward the relevant cap.

## 9. Buy-order actions

Possible states include `KEEP BID`, `UPDATE BID`, `STOP BIDDING`, and
`CANCEL BID`.

- `KEEP BID`: order remains competitive enough and economically valid.
- `UPDATE BID`: a higher bid may be justified **only if** the new bid remains at
  or below max bid and the expected incremental benefit justifies losing queue
  position/attention.
- `STOP BIDDING`: current market economics no longer justify chasing; may leave
  an existing order in place if immediate cancellation is not beneficial.
- `CANCEL BID`: existing committed capital should be released because the
  scenario has become invalid, risk limits are breached, or opportunity cost is
  clearly superior elsewhere.

Recommendation reasons must distinguish economic invalidity from mere
outbidding.

## 10. Sell-listing actions

Possible states include `LIST`, `LEAVE SELL LISTING`, `SELL PARTIAL`, and
`SELL`.

Do not recommend cancel/relist merely because another seller undercut by one
copper. Consider:

- already-paid listing fee;
- current queue/order position;
- current and historical spread/liquidity;
- price difference versus existing listing;
- expected time/capital benefit;
- position/investment thesis.

Relisting is justified by a meaningful economic improvement, not cosmetic
price leadership.

## 11. Opportunity cost

Capital tied in current bids, slow inventory, and long-term positions has an
opportunity cost. Over time, personal realized profit/day and capital turnover
should help distinguish attractive-looking but slow markets from repeatable
markets that actually compound the user's capital.

Personal evidence only affects ranking above explicit sample thresholds and
must expose its sample size/recency.

## 12. Personal accounting

Completed buys create cost-basis inventory lots. Completed sells consume lots
using the accepted accounting policy (initially FIFO). Partial fills and one-to-
many/many-to-one matches must be supported.

Unknown acquisitions must not silently receive zero basis. Realized and
unrealized P&L are separate concepts and screens.

## 13. Investment/seasonal positions

Medium/long-term positions may have a thesis, target prices, and staged exit
plan. Recommendation logic must show historical price/supply/liquidity evidence
and opportunity cost without claiming that an event/season guarantees future
appreciation.

## 14. Crafting economics

Owned tradable materials are not free. Their economic input cost reflects the
accepted opportunity-value policy. Mixed owned/purchased inputs, bound items,
unknown prices, output fees, liquidity, and recipe feasibility must all be
explicit.

A craft is not profitable merely because output sale price exceeds purchased
ingredient price.

## 15. Recommendation vocabulary and evidence

Primary actions include:

`BUY`, `BUY SMALL`, `WAIT`, `KEEP BID`, `UPDATE BID`, `STOP BIDDING`,
`CANCEL BID`, `LIST`, `LEAVE SELL LISTING`, `HOLD`, `REDUCE`,
`SELL PARTIAL`, `SELL`, `SKIP`, `REVIEW`.

Each action should include applicable:

- item;
- quantity and capital;
- current bid/ask;
- max allowed bid;
- exact modeled net profit/ROI;
- current depth/liquidity evidence;
- historical confidence/sample coverage;
- personal evidence when valid;
- portfolio/risk impact;
- plain-language reasons.

Insufficient or contradictory evidence yields `WAIT`, `REVIEW`, or `SKIP`
rather than invented certainty.
