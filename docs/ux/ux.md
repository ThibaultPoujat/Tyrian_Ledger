# UI/UX Specification

## Product interaction goal

Tyrian Ledger should let the user understand personal performance and decide the
next manual trading actions quickly, while keeping detailed evidence available
without turning every screen into a spreadsheet.

The primary daily workflow is the M19 `What Should I Do?` screen. Dashboard and
scanner are supporting evidence/work surfaces, not competing home pages.

## Visual direction

Modern analytical dashboard with restrained Guild Wars 2-inspired atmosphere
without copying ArenaNet assets.

Use:

- dark/charcoal base;
- subtle metallic/parchment surfaces;
- restrained cyan/teal accents;
- clear gold/copper emphasis only for economic values;
- high information density with strong grouping/hierarchy;
- simple icons/CSS shapes rather than copied game assets.

## Main screens

### What Should I Do?

Prioritize explicit actions such as:

`BUY`, `BUY SMALL`, `WAIT`, `KEEP BID`, `UPDATE BID`, `STOP BIDDING`,
`CANCEL BID`, `LIST`, `LEAVE SELL LISTING`, `HOLD`, `REDUCE`,
`SELL PARTIAL`, `SELL`, `SKIP`, `REVIEW`.

Each card/row should make the action, item, quantity/capital, important
price/max-bid value, modeled result, key risk/confidence reason, and why-now
logic scannable. Detailed depth/history/score components may expand on demand.

### Personal dashboard

Show:

- API/sync status and history coverage;
- realized 7/30/90 performance;
- open cost basis/unrealized value separately;
- capital in current buy orders;
- current sell listings;
- recent trades and useful personal statistics;
- clear unknown/insufficient-history states.

### Live scanner

Show/filter current candidates by exact economics and liquidity evidence:

- current bid/ask;
- max bid;
- modeled net profit/ROI;
- order-book/depth quality and flags;
- freshness;
- suggested capital evidence available at current milestone;
- watchlist controls.

Raw ROI should not visually dominate risk/liquidity context.

### Opportunity detail

Show:

- exact acquisition/exit assumptions;
- fee breakdown;
- modeled profit/ROI;
- max bid/break-even context;
- capital required/sizing constraints;
- order-book impact/depth;
- historical persistence/volatility/sample coverage;
- personal evidence when sufficiently sampled;
- risk/anomaly flags;
- freshness.

### Current orders

Make current bid/list price versus market state visible, but distinguish
`outbid` from `economically invalid`. Do not visually encourage one-copper bid
or sell-list churn when the recommendation engine says leave/wait.

### Investments

Track thesis, position, known/unknown basis, targets, current net liquidation
value, unrealized result, historical evidence, and staged exit actions.

### Crafting detail

Show output -> recipe -> intermediate ingredients -> source strategy as an
expandable tree/graph. Highlight owned materials while displaying their
**opportunity cost**, not zero cost. Surface feasibility/truncation/unknowns.

### Account/settings/data

- API-key connection state and safe permission status;
- sync/collection health and manual refresh controls;
- risk/ROI/profit/cash-reserve/sampling/alert settings;
- watchlist/data coverage/storage information;
- backup/restore/clear-local-data actions with explicit destructive confirmation.

The API key value itself never appears in normal UI once stored.

## Interaction rules

- Do not hide critical assumptions only in tooltips.
- Use `Modeled profit`, `Observed median`, `Estimated fill interval`, or similar
  qualifiers where certainty differs.
- Show data age/coverage wherever freshness matters.
- Never show a `guaranteed` profit/fill/price state.
- Unknown/insufficient data is a first-class visual state, not zero or blank.
- Keep filters/selections stable when practical.
- Expose recommendation reasons and binding risk constraints.
- Destructive backup/restore/clear operations require explicit understandable
  confirmation/error recovery.
- Support keyboard navigation, semantic controls, sensible focus, and WCAG 2.2
  AA contrast.

## Desktop-first

Primary optimization is desktop. Responsive behavior should prevent unusable
overflow at narrower desktop/tablet-like widths. Full mobile optimization may
be deferred unless a future ticket explicitly prioritizes it.
