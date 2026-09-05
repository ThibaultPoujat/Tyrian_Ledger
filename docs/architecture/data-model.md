# Data Model - Local Personal Trading Assistant

This is the target conceptual model. Individual tables/columns are introduced by
versioned migration tickets; the document does not authorize implementing every
entity at once.

## 1. Data ownership classes

### Authoritative local inputs

Data captured from external/user sources and preserved as evidence:

- account profile identity/scope;
- completed Trading Post transactions;
- current-order observations;
- public market snapshots/order-book observations;
- user settings/watchlists;
- user-entered investment position metadata where applicable.

### Derived rebuildable state

- FIFO lots and lot matches;
- realized/unrealized statistics;
- historical market metrics;
- opportunity score components;
- recommendations;
- personal fill/turnover estimates.

Derived state may be persisted for performance/audit only when its input/rule
version is stored or it can be safely rebuilt.

### Secrets

The ArenaNet API key is **not database data**. It belongs to the OS-backed secret
store defined by ADR-006.

## 2. Common invariants

- Internal primary keys may be local database IDs.
- External ArenaNet transaction IDs receive unique constraints in account scope.
- All timestamps are UTC; preserve source timestamps separately from observation
  timestamps when both exist.
- Prices, costs, fees, and profit use integer copper.
- Quantities are integral and validated non-negative/positive according to
  entity semantics.
- Account-scoped tables include an explicit local account profile key.
- Unknown values are nullable/explicit states, never invented zeroes.
- Schema changes use versioned migrations with upgrade tests.

## 3. Initial entities

### AccountProfile

Purpose: local scope for one connected account without storing the secret.

Conceptual fields:

- local account profile ID;
- safe stable account identifier/name when the verified API permits it;
- created UTC;
- last successful sync UTC;
- permission/status metadata safe to persist;
- history coverage start/end metadata.

### PersonalTpTransaction

One completed buy or sell event from the authoritative personal Trading Post
history.

Conceptual fields:

- account profile ID;
- external transaction ID (unique in account scope);
- side (`Buy`/`Sell`);
- item ID;
- quantity;
- unit price copper;
- source-created/source-completed timestamps where verified;
- first imported UTC;
- last seen UTC;
- source/schema version metadata.

Never silently mutate an old completed event into a different economic event.

### CurrentTpOrderObservation

Represents what the application observed as currently open at a sync point.

Conceptual fields:

- account profile ID;
- external order/transaction identifier when available;
- side;
- item ID;
- quantity/remaining quantity according to verified contract;
- unit price copper;
- source timestamps when available;
- observed UTC;
- sync batch ID/status.

Current state may be materialized separately, but observation history becomes
useful for later fill-time estimation.

### ItemMetadata

Normalized public metadata needed for names/display/tradability decisions.
Reference data has its own refresh policy and source version/freshness.

### UserSettings

Versioned local policy/configuration such as:

- minimum ROI/profit filters;
- risk profile / explicit sizing limits;
- cash reserve target;
- sampling intervals;
- alert preferences;
- active recommendation-policy version.

### WatchlistEntry

Item/market approved for higher-interest sampling or research. May include tags,
notes, desired sampling tier, or strategy category.

## 4. Accounting entities

### InventoryLot

Derived from completed acquisitions with known basis.

- account profile ID;
- item ID;
- source buy transaction ID;
- original quantity;
- remaining quantity;
- acquisition basis copper;
- acquisition/order timestamps;
- accounting-policy version.

Unknown pre-history inventory is represented separately/explicitly rather than
as a zero-cost lot.

### LotMatch

Maps sold quantity to source lot quantity under FIFO.

- sell transaction ID;
- buy lot/source transaction ID;
- matched quantity;
- allocated acquisition basis;
- sale gross value;
- allocated fees according to canonical policy/source evidence;
- realized net profit;
- accounting-policy version.

Matches are deterministic derived state and should be rebuildable.

## 5. Market-history entities

### MarketSnapshot

Cheap timestamped public observation:

- observed UTC;
- item ID;
- highest buy copper;
- lowest sell copper;
- aggregate buy quantity;
- aggregate sell quantity;
- source freshness/status;
- sampling tier/policy version.

Indexes should support item+time-window queries efficiently.

### OrderBookSnapshot / OrderBookLevel

Optional detailed observation for high-interest/shortlisted items:

- snapshot ID/time/item;
- side;
- unit price copper;
- quantity;
- listing count;
- source freshness/policy version.

Full books are substantially larger and have explicit sampling/retention policy.

## 6. Investment entities

### Position

Tracks medium/long-term or explicitly classified holdings.

- account profile ID;
- item ID;
- strategy/category;
- quantity;
- known/unknown cost basis link/value;
- opened UTC;
- status;
- thesis/notes;
- target levels;
- policy/version metadata.

Partial exits are represented rather than rewriting the original position
history.

## 7. Recommendation/evaluation entities

### RecommendationSnapshot

Versioned audit record of what the deterministic engine recommended at one time:

- generated UTC;
- account/profile scope if personal;
- item/action;
- suggested quantity/capital;
- max bid/current market values;
- modeled profit/ROI;
- score components and risk flags;
- historical/personal sample evidence;
- recommendation-policy/configuration version.

### RecommendationOutcomeLink

Links a recommendation to an observed user-executed trade only when the evidence
supports that association. Unexecuted recommendations remain unobserved; never
invent counterfactual profit.

## 8. Backup and retention

SQLite backup/restore must cover all durable authoritative local data and any
derived records required for audit/history. Retention policy for large market
history is explicit and independently configurable from clearing personal
account data.

No retention job may silently destroy the only copy of personal completed
transaction history.
