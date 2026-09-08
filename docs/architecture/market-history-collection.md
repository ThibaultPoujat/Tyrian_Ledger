# Market-history collection and storage policy

TKT-M18-01 establishes durable raw public-market evidence. It defines storage
and collection intent only; TKT-M18-02 owns the background scheduler, request
budget, retry/backoff behavior, and collector health, while TKT-M18-03 owns any
retention or downsampling operation.

## Raw evidence

`market_price_observations` is append-only aggregate top-of-book evidence. Each
row records the UTC observation time, item ID, highest buy and lowest sell in
integer copper, aggregate buy/sell quantity, complete-source status, sampling
tier, and policy version. `(item_id, observed_at_utc)` is unique and the same
columns are indexed for item/window reads.

Full order books are intentionally separate. An explicitly opted-in capture
creates one `market_order_book_snapshots` row and immutable ordered buy/sell
levels in `market_order_book_levels`; best-price sampling never creates either
table's rows. The M18 policy presently allows detailed books only for an
explicit high-interest source, not broad-universe screening.

Only `Complete` source status is persistable in M18. A failed, partial, stale,
or malformed upstream read has no raw observation to write. Future compatible
source classifications require a new migration and policy version rather than
silently reinterpreting existing evidence.

## Sampling defaults

| Tier | Source | Default interval | Detailed book |
| --- | --- | --- | --- |
| Current personal order | Last complete local current-order snapshot | 15 minutes | No |
| Watchlist | Local approved markets | 30 minutes | No |
| Broad market | Later registered screening source | 6 hours | No |

Sources are typed and independently registered. When multiple sources name an
item, the highest-priority tier wins; a detailed book needs an explicit opt-in
from a source at that winning high-interest tier. Held positions are not a
source in M18; TKT-M20-03 owns their registration and removal.

## Local collection operation

The local host starts one collector worker after local SQLite initialization.
It immediately evaluates registered sources, then re-evaluates local source
membership every configurable minute by default. The worker only sends gateway
requests for targets whose latest retained aggregate observation is due at the
winning tier's interval. This lets a restart resume the existing cadence and
lets future registered broad-market sources participate without a scheduler
change.

The collector uses only `IGw2ApiClient`, so its requests retain the typed
gateway's 200-ID batching, shared request budget, cancellation, deduplication,
and bounded 429/temporary-failure behavior. A complete aggregate response is
append-only evidence; all aggregate observations accepted in one collection run
are persisted as one transaction, so a duplicate or write failure leaves that
run without a partial aggregate capture. A failed, partial, malformed, or
cancelled aggregate response appends nothing. When a separately requested full-book response
fails, its valid aggregate observation may still be retained, but no book row
or levels are written.

`GET /api/market-history/collector` exposes no-store, browser-safe operational
health (last successful capture, stable failure category/count, tracked count,
and next run). `POST /api/market-history/collector/run` performs one serialized
manual run and is subject to the same local origin protection as every other
state-changing endpoint. Neither endpoint exposes credentials, request headers,
or raw upstream payloads.

## Representative storage estimate

This is a planning estimate, not a SQLite file-size guarantee. It assumes 20
current-order items, 100 watchlist items, 10,000 broad-market items, 30 days,
and roughly 128 bytes per aggregate row including its item/time index and page
overhead:

| Class | Formula | Observations/day | Estimated growth |
| --- | --- | ---: | ---: |
| Current orders | 20 x 96/day | 1,920 | 0.23 MiB/day |
| Watchlist | 100 x 48/day | 4,800 | 0.59 MiB/day |
| Broad market | 10,000 x 4/day | 40,000 | 4.88 MiB/day |
| Aggregate total | 46,720 x 128 bytes | 46,720 | about 5.70 MiB/day; 171 MiB/30 days |

Detailed books are deliberately much more expensive: 20 opted-in items at the
15-minute interval with 200 retained levels each yields 384,000 levels/day.
At a planning allowance of 80 bytes per level including indexes, that is about
29 MiB/day (870 MiB/30 days), plus snapshot rows. This is why full-book capture
is never a side effect of aggregate collection. Actual usage depends on SQLite
page fill, value sizes, indexes, and the number/depth of selected books; M18-03
will expose measured usage before any retention policy is introduced.

Raw rows are not rewritten, aggregated, or deleted by this ticket. A future
downsampling or retention migration must retain its own versioned rules and
must not corrupt or reinterpret the existing raw evidence.

## Retention and integrity policy

M18-03 establishes retention policy version 1 separately for aggregate
best-price observations and detailed order-book captures. Both policies are
`PreserveAllRawEvidence`: the application performs no automatic deletion,
rewriting, or downsampling of either evidence class. Measured database size and
coverage are exposed through the local `GET /api/market-history` status endpoint
so a later policy can be proposed from actual usage rather than planning
estimates.

The status endpoint accepts an optional `itemId` and a paired UTC
`fromInclusiveUtc`/`toInclusiveUtc` window. It reports database-file size,
aggregate/book counts and observed ranges, the two policy identities, and an
on-demand integrity result. Integrity validation checks SQLite integrity,
schema/index/foreign-key consistency, UTC timestamp storage, duplicate-key
constraints, and persisted market value invariants. A failed check is reported
without attempting to repair, delete, or reinterpret retained evidence.

Any future destructive retention or downsampling change requires an explicit
owner-approved policy and a new versioned migration that preserves its stated
raw window and derived aggregate semantics. Backup/restore continues to copy
all market-history tables, while clear-personal-data remains limited to
account-scoped data and does not clear public market history. Application-created
backups retained in the managed backup directory can be restored locally without
the imported-file browser upload limit, so that limit does not become a
retention or recoverability boundary for accumulated history.
