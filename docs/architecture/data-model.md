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

M20 derives no new durable timing state from these rows. Its deterministic
rebuild retains source timestamp durations separately from local
interval-censored confirmation windows: the latter run from the last snapshot
that contained a strictly compatible order to the completed event's first local
import. A later snapshot that omits an order remains unknown unless compatible
completed-history evidence confirms it; it is never converted into a fill.

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

### M14 implemented SQLite schema

M14-01 introduces the first durable schema through ordered transactional
migrations. The migrator initializes `schema_migrations` before applying
migrations. Migration 1 creates `account_profiles` and
`completed_tp_transactions`; migration 2 adds
`current_order_sync_batches`, `current_tp_orders`,
`current_tp_order_observations`, `item_metadata`, and `user_settings`.
Migration 3 adds safe sync-attempt status and observed completed-history
coverage to `account_profiles`.

- `account_profiles.account_scope_id` is the opaque account scope and is unique;
  no account name or credential is stored.
- `completed_tp_transactions` is unique by
  `(account_profile_id, external_transaction_id)`. Its normalized side, item,
  quantity, integer-copper unit price, source timestamps, and import
  timestamps are immutable except for `last_seen_at_utc` on an identical
  repeat import.
- Each successful complete current-order snapshot has one
  `current_order_sync_batches` row, immutable
  `current_tp_order_observations`, and an atomically replaced
  `current_tp_orders` materialization. Observation history is not a retention
  policy and is not cleared by this ticket.
- `item_metadata` stores only normalized item ID, display name, and observation
  timestamp. `user_settings` is a singleton typed non-secret record containing
  a settings version plus nullable integer-copper and basis-point policy inputs;
  it is not a generic key/value or arbitrary JSON store.
- All persisted timestamps are UTC round-trip values. Foreign keys protect
  account ownership; prices and money-like settings use integer copper.
- A successful M14-02 sync writes completed-history upserts, current-order
  observations/materialization, item metadata, and successful sync/coverage
  status in one transaction. A failed remote read records only a stable error
  category and attempt time after the opaque scope is known; it never deletes
  completed history, current orders, metadata, or prior successful coverage.

### M21 account-crafting snapshots

Migration 8 adds the account-profile-scoped, current-only
`account_crafting_snapshots`, `account_crafting_bank_entries`,
`account_crafting_material_entries`, `account_crafting_recipe_unlocks`, and
`account_crafting_disciplines` tables. They store normalized quantities,
binding evidence, unlock IDs, and aggregated discipline capability only; raw
responses, character names, and credentials are never retained. Replacing a
snapshot deletes its prior child rows transactionally, so the database does
not accumulate a private account-inventory history.

### M21 execution plans and local shadow

Migration 9 adds the account-profile-scoped `execution_plans` table. Its
validated JSON payload contains the typed plan, explicit reservations, ordered
manual steps, and local reversible execution-shadow events; it never stores an
ArenaNet credential, raw account payload, or browser-owned financial result.
The row is updated transactionally with each local transition, while the event
sequence remains in the plan payload for deterministic reconstruction and later
reconciliation with verified account evidence.

Migration 11 adds `plan_completion_receipts`, keyed by account profile, plan,
and logical command ID. A receipt records the submitted step/revision/operation
and canonical quantity/price, committed revision, and resulting event identity
when the operation produced an event. The plan transition and receipt are
committed in the same SQLite transaction. Receipts remain available while their
account data is retained, are included in ordinary database backup/restore, and
are cleared before plan/account rows during explicit personal-data reset.

Reconciliation inputs use an application `PlanEvidenceFrame`; the frame itself
is not persisted. It carries the trusted account scope, explicit UTC evaluation
time, and separate source provenance for physical inventory, coin, current TP
orders, and completed transactions. Provenance distinguishes local fetch time
from optional upstream observation time and retains capture identity,
availability, completeness, and coverage. The plan JSON stores only the
resulting evidence-consumption identities, per-event negative-observation
progress, and source-capture high-water marks needed to reject replayed or
out-of-order frames under the existing revision/CAS writes.
The current bank/material producer is partial because it omits all-character
inventory. Atomic TP synchronization shares one local capture identity across
its order and transaction sources when all required pages succeeded; it does
not establish upstream cache freshness. Craft changes remain provisional until
a producer supplies complete affected-item coverage with an upstream
observation time. Contradicted plans retain a structured reconciliation reason
code with durable state, and the plan response exposes it for French rendering
at the UI boundary. VERIFY-008 remains open for external cache uncertainty.

No migration in M14 creates a credential, API-key, authorization,
token, raw-upstream-payload, accounting, market-history, position, or
recommendation table.

### M17 watchlist schema

TKT-M17-03 adds `watchlist_entries` through migration 4. It is a local-user
table (not an account or credential table): `item_id` is the positive primary
key and `added_at_utc` records when the market was approved. It is retained by
clear-personal-account-data alongside local settings, and participates in the
existing local backup/restore workflow.

### M18 market-history schema

TKT-M18-01 adds migrations 5 and 6. `market_price_observations` is immutable
aggregate top-of-book evidence keyed uniquely by item and UTC observation time;
it retains integer-copper prices, aggregate quantities, complete-source status,
sampling tier, and policy version. Its item/time index supports historical
window reads. Optional `market_order_book_snapshots` and immutable ordered
`market_order_book_levels` are separate tables with their own item/time and
snapshot indexes, so detailed depth is stored only after explicit policy opt-in.
Migration 6 removes redundant explicit indexes already covered by uniqueness
constraints. The repository rejects zero-price order-book levels for all new
captures; schema-valid version-5 zero-price rows are retained unchanged as
legacy raw evidence so migration and restore never rewrite or discard history.
The full collection policy and representative storage-growth estimate are in
`docs/architecture/market-history-collection.md`.

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

The SQLite implementation stores the position header in
`investment_positions`, immutable manual exit rows in
`investment_position_exits`, and staged levels in
`investment_position_targets`. These tables are account-profile scoped through
the position header; they contain no credentials and are cleared with other
personal account data while backups retain them for recovery.

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


## 9. Protected holdings evidence (P02C)

`account_holdings_snapshots` stores one account-profile-owned, generation/store-scoped
normalized document. It contains typed source/actor/location observations, independent
fetch intervals and coverage, recipe/crafting/equipment facts, positive public item
categories, account protection rules, a durable protective item-ID floor and explicitly
stale rows. It contains no raw API payload, secret or immutable-instance claim.
Relational ownership, monotone capture start and document validation are enforced
alongside schema/foreign-key/integrity checks during backup restore. Clear includes
this table; backup includes it automatically. Generation mismatch leaves restored or
restarted evidence non-admissible until a new guarded refresh.

Portfolio lots and completed TP history keep accounting/cost provenance; they add
no physical quantity. One application projector selects at most one eligible location
per item using maximum count and deterministic source/actor/slot ties, then applies
account retention once. Delivery stays uncollected. Positive inventory/cash shadows remain
provisional and are excluded from physical/wallet admission. Negative residual effects and
global item reservations constrain all plans. Plan documents retain selected coordinates,
binding and real actor/recipe commitments; private SQLite start/completion/reconciliation
revalidate against the latest stored projection in the same lease/transaction.
Locations confer no cost basis or transfer proof, and live physical frames remain
Partial with independent source clocks while VERIFY-017 is open.
