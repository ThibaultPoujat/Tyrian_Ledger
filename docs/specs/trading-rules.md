# Trading Rules and Recommendation Policy

This document defines the behavioral rules that financially authoritative code
must implement. VERIFY-013 remains open after TKT-M15-01: ArenaNet support
documents the non-refundable 5% listing fee and the 10% exchange fee, and the
linked official wiki documents a 1-copper minimum for each. Neither source
defines fractional-copper rounding. The canonical application policy therefore
retains the owner-approved per-fee round-up behavior as a **modeled/provisional**
assumption rather than verified external behavior.

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
verification of the real GW2 rounding contract. The canonical policy explicitly
reports that its fractional-copper rounding is not externally verified.

Listing fees matter twice to behavior:

1. they reduce modeled profit;
2. the externally documented non-refundable listing fee is lost on cancellation,
   and relisting pays a new listing fee in addition to losing queue position.

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

### Historical market-metric semantics

Historical market research is descriptive evidence, never a price, fill, or
profit prediction. Version one calculates each eligible aggregate observation
using the same proposed-price convention as the current scanner: highest buy
plus one copper and lowest sell minus one copper. The canonical fee policy then
derives modeled net ROI from integer-copper acquisition, listing fee, exchange
fee, and profit inputs. The latest eligible retained observation is labeled
`latest observed`; it is not a new live read.

The latest-observed lookup is independent of the bounded trailing-window read,
so retained valid evidence older than 30 days remains visible when no newer
eligible observation exists. It is still one retained observation, not a live
price or a forecast.

The fixed 7-day and 30-day UTC windows are inclusive. They are available only
when they contain at least 20 and 60 eligible observations respectively and
the first-to-last eligible observation spans at least 80% of the requested
duration. Every result reports its exact bounds, raw/eligible/excluded sample
counts, observed-span percentage, and largest eligible gap. Tradable ROI and
depth metrics require strictly positive aggregate buy and sell quantities;
zero-side, missing, or legacy-invalid observations are excluded from metrics
and remain visible in coverage. The application never fills, interpolates, or
creates missing observations.

The initial disclosed ROI thresholds are 15% and 20% (1,500 and 2,000 basis
points), compared exactly against integer-copper ROI numerators and
denominators. Sufficient windows report median net ROI, threshold rates,
positive-ROI persistence, median aggregate quantities, observed price ranges,
and maximum sell-price drawdown. Buy/sell prices, raw sell-to-buy spread ratio,
and minimum-side aggregate depth use population coefficient of variation as
their stability measure. These unitless volatility and drawdown values use
IEEE-754 numeric precision; ROI medians and percentages use decimal statistical
ratios, with the even-sample median defined as the arithmetic midpoint of the
two central ordered ROI values. Monetary inputs and returned monetary values
remain integer copper.

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

### Opportunity-score policy version 1

The first opportunity-score policy is a deterministic application-layer
comparison of already-calculated current scanner evidence and historical
analytics. It does not fetch data, size a position, choose an action, or predict
a future price or fill. Results expose the policy version, rank, base points,
applied penalty, final points, historical-confidence state, every named
component, and every anomaly flag. Scores are comparative decision-support
evidence, not probabilities.

The base score is bounded to 100 points:

- expected economics contributes at most 25 points. Sixty percent of that
  component is current exact net ROI normalized linearly to a 30% ceiling; forty
  percent is modeled absolute net profit normalized linearly to a 10,000-copper
  ceiling. Values beyond either ceiling do not add points;
- current liquidity contributes at most 25 points. Forty percent uses the
  smaller aggregate side normalized to ten times intended quantity, forty
  percent uses the smaller near-best side normalized to intended quantity, and
  twenty percent uses the smaller near-best listing count normalized to three;
- historical persistence contributes at most 20 points, split equally between
  the percent of positive-net-ROI observations and the mean of the disclosed
  historical ROI-threshold rates;
- historical stability contributes at most 15 points. It averages the inverse
  quality of buy-price, sell-price, spread-ratio, and minimum-side-depth
  population coefficients of variation. Each measure falls linearly from full
  quality at zero to no quality at a coefficient of variation of 0.5;
- historical confidence contributes at most 15 points: five for an available
  7-day window and ten for an independently available 30-day window;
- personal fill/turnover evidence is explicitly `NotYetAvailable` and has zero
  weight until a later ticket supplies sufficiently sampled evidence.

The available 30-day window is the comparison baseline; the available 7-day
window is used only when 30-day evidence is insufficient. With neither window,
persistence and stability contribute zero and confidence is `Insufficient`.
Exactly one available window is `Partial`; both are `Strong`. Missing coverage
is flagged but receives no additional penalty because its effect is already
represented by the unavailable components and lower confidence.

Version one flags current ROI that is both at least 20 percentage points and at
least twice the non-negative historical median; near-best quantity below the
intended quantity or fewer than three near-best listings; an existing current
price cliff; current buy or sell prices more than 20% outside their historical
ranges; aggregate buy or sell quantity more than 50% away from its historical
median; and intended quantity above the current visible-depth participation
cap. The respective penalties are 10, 10, 5, 5, 5, and 15 points, with separate
5-point price-spike and price-drop flags where applicable. Total applied
penalties are capped at 40 points and the final score is clamped to 0-100.

All component values and totals are decimal values rounded to four decimal
places away from zero. Candidate order cannot affect results. Ranking sorts by
final score descending, then modeled profit descending, then item ID ascending.
The fee-derived inputs remain modeled and provisional while VERIFY-013 is open.

## 8. Position sizing and bankroll protection

Suggested size is bounded by the minimum of independently explainable caps:

- deployable cash after reserve;
- single-market exposure cap;
- liquidity/order-book participation cap;
- existing open order/position exposure;
- strategy/category concentration cap;
- speculative/illiquid cap where applicable.

### Position-sizing policy version 1

The disclosed, configurable default policy reserves 15% of total bankroll,
caps high/medium/low-liquidity item exposure at 5%/3%/1.5%, caps a strategy at
20%, and caps a category at 25%. All percentages use integer basis points.
Reserve rounds up to preserve safety; cap money and affordable quantities round
down so a suggestion cannot exceed a cap.

Version one receives a complete explicit portfolio snapshot: available cash and
non-negative capital-at-risk entries for current orders and held positions.
Total bankroll is available cash plus those entries. Every entry and candidate
must name a strategy and category; incomplete, duplicate, unknown, negative,
or otherwise invalid evidence produces no allocation rather than silently
bypassing a limit. Callers must not represent the same committed capital as both
an order and a position.

Candidates are allocated in disclosed score-rank order (then item ID). Each
suggestion is the minimum of remaining cash after reserve, remaining item,
strategy, and category capacity, and the scanner's visible participation cap.
After each suggestion its modeled capital is counted before the next candidate
is sized, so a group of otherwise attractive candidates cannot compose around
the reserve or concentration limits. Every returned allocation exposes all cap
quantities and every constraint tied for the minimum.

Capital per suggested unit uses the scanner's existing authoritative one-unit
`TotalCost`, including the modeled listing fee. This is deliberately
conservative for quantity scaling while VERIFY-013's fractional-copper fee
rounding remains open; it is not a fill, profit, or execution guarantee.

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

FIFO rebuilds use completed transactions only and isolate inventory by local
account profile and item. Transactions are ordered by completed UTC timestamp,
then by ascending external transaction ID when timestamps are equal. A sale is
never retroactively matched to a buy that sorts after it.

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
