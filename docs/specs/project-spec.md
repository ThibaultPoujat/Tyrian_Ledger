# Project Specification - Tyrian Ledger

## 1. Product statement

The system is a local, browser-based decision-support tool for Guild Wars 2 Trading Post and crafting analysis. Its purpose is to help a single user identify potentially profitable in-game gold-making opportunities using legal, read-only analysis.

The application is not a trading bot, gameplay bot, order executor, browser automator, or autonomous game agent.

## 2. Product principles

1. **Read-only by design.** The application may request permitted read-only GW2 API resources. It MUST NOT automate Trading Post actions or gameplay.
2. **Deterministic financial truth.** Money calculations, fee calculations, opportunity ranking, and crafting feasibility are performed by deterministic application code. A future LLM MAY explain results but MUST NOT become the source of numerical truth.
3. **Data freshness is first-class.** Every market-derived result carries capture time and a data-quality/freshness state.
4. **API minimization.** All GW2 requests go through one server-side gateway with caching, batching, deduplication, scheduling, and retry/rate-limit handling.
5. **Explicit assumptions.** A result is a scenario, not a guarantee of execution or profit.
6. **Opportunity cost matters.** Owned items are never treated as free inputs by default.
7. **Local-first.** No server, account system, cloud database, or mandatory remote service is required for v1.
8. **Testability.** Core calculations are pure and independently testable.
9. **Secure by default.** The API key stays server-side/local and outside version control.
10. **Incremental complexity.** Features requiring historical data are deferred until local collection has created enough evidence.

## 3. Scope

### MVP scope

- current market price and order-book ingestion;
- caching and request scheduling;
- fee-aware flipping scenarios;
- order-book depth simulation;
- opportunity ranking using deterministic scoring;
- user constraints: capital, strategy preference, minimum profit, risk tolerance, capital allocation, data freshness;
- local account connection with minimum required API permissions;
- account data snapshots for bank/materials and relevant character crafting information;
- limited-depth crafting profitability;
- owned-material opportunity-cost analysis;
- session planning using coarse effort categories;
- modern desktop browser UI;
- comprehensive unit/integration/fixture tests;
- local SQLite persistence;
- clear read-only disclosures and data freshness indicators.

### Explicitly out of MVP scope

- any action that places/cancels TP orders;
- gameplay automation;
- browser automation against the game or TP;
- a public/multi-user server;
- cloud persistence;
- mandatory LLM integration;
- precise execution-time prediction;
- price forecasting marketed as reliable prediction;
- long-term investment recommendations based only on snapshots;
- unrestricted full-world crafting path optimization;
- automatic profit reconciliation with in-game state beyond available read-only transaction/account data;
- mobile-first layouts.

## 4. Users

Initial user: one human developer/player operating the tool locally on their own Mac.

No user-to-user sharing is required for v1.

## 5. Main user journeys

### Journey A - Market flip scan

1. User opens the local dashboard.
2. Application shows when market data was last refreshed.
3. User chooses capital and risk/strategy preferences.
4. Application selects candidate items from available market data.
5. For each candidate the deterministic engine calculates scenario profit, ROI, capital need, price impact, liquidity proxy, and data freshness.
6. UI ranks and filters opportunities.
7. User opens an opportunity to see assumptions and calculation details.

### Journey B - Account-aware crafting

1. User supplies an API key with the minimum necessary scopes.
2. Backend validates the token and reports available permissions without exposing the secret.
3. Account snapshots are fetched only when needed.
4. Crafting analyzer evaluates feasible recipes.
5. Owned materials are valued economically rather than at zero.
6. UI compares buy-all, use-owned, and mixed strategies when data permits.
7. User sees the exact intermediate path used by the calculation.

### Journey C - Session plan

1. User chooses available capital, preferred strategy, desired risk, and an effort category.
2. Engine filters opportunities that do not fit.
3. Planner builds a ranked set without claiming exact execution time.
4. User can save selected opportunities to local history.

## 6. Definitions

### Profit

Profit is the modeled net economic gain for a specific scenario after explicitly modeled transaction fees and acquisition costs.

### Realized profit

Once history exists, realized profit is based only on recorded actual acquisition/sale values and actual applicable fees, not current market value.

### Unrealized P/L

A separate statistic comparing current modeled value to recorded cost basis. It MUST NOT be mixed with realized profit.

### Liquidity

A proxy derived from current order-book depth and price impact. It is not a guaranteed execution probability.

### Confidence

A qualitative or component-based indication of evidence quality. It MUST NOT be represented as a mathematically precise probability unless a validated statistical model exists.

## 7. Non-functional requirements

- Local application starts without an external server.
- Default bind address MUST be loopback-only.
- No API key is sent to browser JavaScript.
- No API key is written to logs.
- Core calculations are deterministic for identical inputs and configuration.
- Tests MUST run without live GW2 API access.
- UI MUST support current major desktop browsers. Exact browser matrix is a release configuration item.
- Accessibility: keyboard navigation, readable contrast, semantic controls, sensible focus management.
- No decorative reuse of GW2 proprietary UI assets.

## 8. API verification policy

Exact endpoint schemas, scopes, current quotas, cache guidance, and field meanings are external contracts and MUST be revalidated before release.

Initial authoritative/community references to verify:

- https://wiki.guildwars2.com/wiki/API:2/commerce/prices
- https://wiki.guildwars2.com/wiki/API:2/commerce/listings
- https://wiki.guildwars2.com/wiki/API:2/commerce/transactions
- https://wiki.guildwars2.com/wiki/API:2/recipes
- https://wiki.guildwars2.com/wiki/API:2/recipes/search
- https://wiki.guildwars2.com/wiki/API:2/account/bank
- https://wiki.guildwars2.com/wiki/API:2/account/materials
- https://wiki.guildwars2.com/wiki/API:2/characters/:id/crafting
- https://wiki.guildwars2.com/wiki/API:2/account/recipes
- https://wiki.guildwars2.com/wiki/API:2/tokeninfo
- https://wiki.guildwars2.com/wiki/API:Best_practices
- https://wiki.guildwars2.com/wiki/API:Terms_of_Use

If an exact field, quota, fee behavior, or permission is uncertain, the implementation MUST mark it VERIFY and stop short of treating the assumption as authoritative.

## 9. Market analysis requirements

### Top-of-book

The engine MAY use current aggregate prices for cheap broad candidate screening.

### Order-book analysis

Detailed listings SHOULD be used for candidates that survive screening. The engine SHOULD simulate quantity-aware acquisition and liquidation across multiple price levels.

### Fee configuration

Fee rules MUST be centralized in a configurable policy. The application MUST NOT scatter magic constants through analytical code.

### Data freshness

Thresholds MUST be configurable. Suggested initial states are Fresh, Aging, Stale, Incomplete. These labels are product policy, not API facts.

## 10. Crafting requirements

The analyzer MUST respect available recipes, disciplines/ratings, recipe availability, and ingredient quantities when those facts are accessible.

For a multi-step path the engine MUST:

- detect cycles;
- impose a configurable maximum depth;
- memoize repeated subproblems;
- stop or truncate gracefully when the search space becomes too large;
- expose when analysis was truncated;
- compare purchased ingredient economics against owned-item opportunity cost.

## 11. Session planning requirements

The initial planner uses categories rather than exact execution promises:

- Very low effort
- Low effort
- Medium effort
- High effort
- Ongoing/patient

The user MAY override category preferences. Precise time estimates require later data collection and calibration.

## 12. History requirements

Local persistence MAY include:

- user preference profile;
- saved opportunities;
- planned operations;
- recorded actual outcomes;
- locally captured market snapshots;
- calculation versions/strategy configuration identifiers;
- account snapshots, with data minimization.

The UI MUST let the user clear local account-related data.

## 13. Future historical investment feature

The application MAY collect sampled market snapshots locally. Historical analysis should not begin as a predictive feature.

First provide descriptive measures such as observed percentile, volatility, spread persistence, drawdown, and liquidity stability. Only later consider predictive strategies, with explicit validation against out-of-sample data.

## 14. Future LLM boundary

No LLM integration is required for the current project. If later introduced, it MUST be an adapter around deterministic application services and MUST NOT have direct access to the GW2 API key or authority to perform mutations.

## 15. Completion standard

A release is not complete because the UI looks correct. A release is complete only when:

- requirements are reflected in tests;
- tests pass from a clean checkout;
- API assumptions are verified/documented;
- no secret is committed;
- rate-limit/caching behavior is observable;
- calculations are reproducible;
- read-only constraints are demonstrably enforced by architecture.
