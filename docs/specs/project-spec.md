# Project Specification - Tyrian Ledger Personal Trading Assistant

## 1. Product statement

Tyrian Ledger is a local-first personal decision-support application for Guild
Wars 2 Trading Post activity. It combines the player's read-only account/Trading
Post data, current public market data, locally accumulated market history, and
deterministic financial rules to answer:

- what the player has actually earned;
- where capital is currently tied up;
- which markets appear economically tradable;
- what manual action is justified now;
- how much capital should be committed;
- which strategies have worked for this player over time.

The application is not a trading bot, gameplay bot, order executor, browser
automator, or autonomous game agent.

## 2. Intended user and product philosophy

V1 is optimized for the owner as a single local user. It should reduce the need
for spreadsheets and repetitive cross-checking while keeping every financially
important conclusion explainable.

The product favors **repeatable capital turnover and controlled downside over
headline ROI**. A stable, liquid market with moderate net ROI may be preferable
to an extreme spread with negligible depth. Recommendations should be useful in
roughly one or two minutes of review, with deeper evidence available on demand.

Tyrian Ledger gets better through owned data rather than opaque prediction. It
records public-market observations and the player's actual outcomes, then uses
those observations as evidence with explicit sample counts and confidence.

## 3. Core principles

1. **Read-only toward Guild Wars 2.** ArenaNet data may inform actions; only the
   human player performs Trading Post/game actions.
2. **Local-first privacy.** Account data, history, settings, and owned market
   history live on the user's computer unless the user explicitly exports a
   backup.
3. **Deterministic financial truth.** Money uses integer copper. Fees, cost
   basis, profit, ROI, allocation, and recommendations are deterministic and
   covered by tests. Until VERIFY-013 is resolved with sufficient external
   evidence, GW2 fee and rounding output is explicitly modeled/provisional;
   tests of the configured model do not make the external behavior verified.
4. **Explainability before cleverness.** Every score/action exposes the evidence
   and rule components that produced it. No runtime LLM or opaque ML model owns
   financial truth.
5. **History is evidence, not prophecy.** Historical medians, persistence,
   volatility, and personal fill/turnover statistics describe observations and
   never guarantee future fills or prices.
6. **Unknown stays unknown.** Missing permissions, incomplete history, unknown
   cost basis, and insufficient samples must remain visible states.
7. **Capital is scarce.** Cash reserve, existing orders/positions, liquidity,
   and concentration constrain otherwise attractive opportunities.
8. **The project learns safely.** Recommendation snapshots and realized outcomes
   may later be compared, but rules change only through reviewed code/config and
   never through autonomous self-modification.

## 4. Main user journeys

### 4.1 Connect and synchronize

The user configures a dedicated ArenaNet API key through an OS-backed local
secret mechanism. The local host validates it, exposes only safe permission
status to React, and synchronizes the minimum required read-only personal data.

Initial personal Trading Post scope includes:

- current buy orders;
- current sell listings;
- completed buy history;
- completed sell history;
- minimal account identity needed to scope local data.

Later crafting scope may add inventory/material storage, crafting disciplines,
and recipe/account unlock evidence when the verified API supports it.

### 4.2 Understand actual performance

The application persists completed transactions idempotently and reconstructs
inventory/cost basis with deterministic FIFO matching unless a later owner ADR
changes the accounting policy.

The user can inspect:

- realized net profit and fees;
- realized ROI where cost basis is known;
- 7/30/90-day realized performance;
- open FIFO cost basis;
- current buy-order capital;
- current sell-listing value;
- current net liquidation value and unrealized P&L, labeled separately;
- data-coverage start and unknown/unmatched historical inventory.

No screen may claim lifetime profit for periods the local database cannot
support.

### 4.3 Scan current markets

The live scanner screens public Trading Post markets using exact fee-aware
profitability, then obtains detailed order books for candidates that justify the
extra requests.

A candidate may expose:

- highest buy and lowest sell;
- proposed bid/list values;
- exact modeled fees, net profit, and net ROI;
- maximum economically allowed bid for the configured target return;
- aggregate quantity and detailed depth;
- price impact for intended quantity;
- freshness;
- shallow-book and anomaly flags;
- suggested capital/quantity constrained by risk policy.

Raw ROI is never sufficient by itself for a high ranking.

### 4.4 Build owned market history

While the local application is running, a scheduler collects timestamped public
market observations according to an adaptive interest policy. High-interest
items include current personal orders, held positions, and watchlist markets.
Broad-universe sampling can be less frequent. Full order books are collected
more selectively than best-price snapshots.

Over time, Tyrian Ledger calculates only from available observations:

- current, 7-day median, and 30-day median net ROI;
- percentage of observations above configured ROI thresholds;
- buy/sell price volatility;
- spread volatility/persistence;
- median quantity/depth and liquidity stability;
- observed range/drawdown where useful;
- sample count and exact observation coverage.

### 4.5 Decide what to do

The primary `What Should I Do?` screen combines personal account state, current
orders, live market evidence, historical evidence, and portfolio risk.

Supported action vocabulary includes:

- `BUY`, `BUY SMALL`, `WAIT`;
- `KEEP BID`, `UPDATE BID`, `STOP BIDDING`, `CANCEL BID`;
- `LIST`, `LEAVE SELL LISTING`;
- `HOLD`, `REDUCE`, `SELL PARTIAL`, `SELL`;
- `SKIP`, `REVIEW`.

Each action provides applicable quantity/capital, current market, max allowed
bid, modeled net profit/ROI, liquidity/depth evidence, historical confidence,
portfolio impact, and plain-language reasons.

The system must not recommend chasing a bid beyond its economic max. It must not
recommend cancel/relist of a sell merely because another player undercut by one
copper when the expected benefit does not justify lost listing fees and queue
position.

### 4.6 Learn from personal outcomes

When enough observations exist, the application may derive approximate personal
fill/holding durations, realized ROI distribution, realized profit per day,
capital turns, and completion rates. API observation limitations must be
explicit; polling intervals must not be presented as exact fill timestamps.

Personal evidence affects ranking only above configured sufficiency thresholds.
Weak samples do not override generic market evidence.

### 4.7 Track investments

Medium/long-term positions are tracked separately from short-term flip workflow
where appropriate. The user can record thesis, strategy, quantity, cost basis,
targets, current net liquidation value, unrealized P&L, and historical
price/supply/liquidity context. Staged actions such as `HOLD`, `SELL PARTIAL`,
and `SELL` are supported without implying that seasonal scarcity guarantees
appreciation.

### 4.8 Analyze crafting later

Crafting analysis treats owned tradable materials as economic assets, not free
inputs. It compares owned-material opportunity value, purchase cost, mixed
strategies, output fees, feasibility, market liquidity, and historical evidence.
Bounded recipe-graph search uses cycle detection, depth/candidate limits, and
memoization; exhaustive world optimization is outside scope.

## 5. Risk and bankroll behavior

Risk policy is configurable and visible. Initial policy design should support:

- a meaningful cash reserve (reference default approximately 15%);
- smaller single-market caps as liquidity falls;
- existing orders and held positions counting toward exposure;
- additional caps based on observed order-book participation/depth;
- category/strategy concentration warnings;
- smaller allocations for speculative/illiquid positions than for highly
  liquid repeatable markets.

These are policy defaults to review and test, not magic constants to scatter
through source code.

## 6. Data ownership and persistence

SQLite is the durable local store. Completed personal transactions must be
preserved after they age out of remote API history. Sync is idempotent and
partial remote failures must not erase previously valid local state.

Backups are explicit local artifacts controlled by the user. No automatic cloud
upload exists in V1.

Derived state such as FIFO matches, statistics, and recommendation records must
be reproducible or versioned from authoritative inputs.

## 7. Non-functional requirements

- Local host binds to loopback by default.
- Host-header validation permits only explicit local host values to prevent
  DNS-rebinding access.
- Production frontend and API are same-origin. Development CORS allowlists only
  exact configured trusted development origins; wildcard origins are forbidden.
- State-changing local endpoints have explicit cross-origin request/anti-forgery
  protection independent of CORS.
- Current desktop Chrome, Firefox, and Safari-compatible browser behavior remains
  a target; automated browser coverage uses the supported Playwright engines.
- UI supports keyboard navigation, semantic controls, sensible focus, and WCAG
  2.2 AA contrast.
- Clean-checkout build/test instructions are maintained.
- Database migrations are versioned and tested.
- Secret scanning remains part of project hygiene.
- Normal tests use fixtures/mocks instead of live ArenaNet calls.
- External API uncertainty is recorded in the VERIFY register.
- No decorative dependence on proprietary Guild Wars 2 UI assets.

## 8. Explicit non-goals

- automated Trading Post order placement/cancellation;
- gameplay automation;
- autonomous capital deployment;
- a runtime LLM making financial decisions;
- opaque machine-learning price prediction;
- guaranteed fill/profit/price claims;
- cloud multi-user account hosting in V1;
- exhaustive high-frequency scraping of every order book;
- treating unknown cost basis or owned materials as free.

## 9. Success checkpoints

The product is progressively useful:

- after M15, personal accounting is trustworthy;
- after M16, the user has a useful personal dashboard;
- after M17, current opportunities can be scanned without manual arithmetic;
- after M18, owned market history accumulates automatically;
- after M19, the central action/recommendation workflow is available;
- M20-M22 deepen personal learning, investment/crafting workflows, reliability,
  and evaluation.

## 10. Completion standard for financially authoritative work

A financial/recommendation ticket is not complete because the UI looks right.
It requires deterministic tests, boundary/edge cases, acceptance-criteria
coverage, an independent fresh-context review, and a short functional summary
that a non-programmer can verify.
