# GW2 API Endpoint and Permission Matrix

Authoritative endpoint table for Tyrian Ledger. Every GW2 request in this
application MUST be one of the endpoints listed below and MUST be routed
through the single typed gateway defined in `docs/architecture/architecture.md`
(ADR-004). No endpoint may be added as an application dependency without first
being added to this table.

## Verification method

- Reviewed live on **2026-08-21** via the Guild Wars 2 Wiki page source
  (`?action=raw`, HTTP 200 for every page listed in References) and the
  rendered pages.
- Facts below that the wiki states explicitly are recorded as-is.
- Facts the wiki does not state, or that may have changed since the last
  review, are marked **VERIFY**. Quota and error-handling policy details
  belong to TKT-M0-03; terms/legal scope belongs to TKT-M0-04.
- Per `docs/specs/project-spec.md` §8 and
  `docs/specs/verified-external-notes.md`, the exact contract MUST be
  revalidated before release (re-run this review in M8).

## Global request rules (from `API:2` and `API:Best_practices`)

- Base URL: `https://api.guildwars2.com/v2/...`.
- Single resource: subpath `/v2/<endpoint>/<id>` or `?id=<id>`.
- Batch: `?ids=<comma-separated>` (array response). Community-documented
  practical limit: **up to 200 IDs per request** (from
  `API:Best_practices`; **VERIFY** the exact hard limit and 206 behavior in
  M2). HTTP **206** is returned when using `ids` if at least one, but not all,
  IDs are valid.
- Paging: `page` / `page_size` (zero-indexed); response headers
  `X-Page-Size`, `X-Page-Total`, `X-Result-Count`, `X-Result-Total`.
  **VERIFY** per-endpoint applicability and max page size in M2.
- Schema version: production requests MUST pin a known schema version via
  `?v=<ISO-8601>` or `X-Schema-Version` (wiki-recommended; **VERIFY** the
  chosen pinned versions per endpoint against `/v2.json?v=latest` in M2).
  If omitted, the API returns the earliest schema.
- Auth: `Authorization: Bearer <API key>` header (server-side; preferred) or
  `?access_token=`. This application uses the header only, from the
  server-side secret store (ADR-006); the key is never sent to the browser.
- Errors: 403 invalid key or missing permission; 404 unknown endpoint or all
  IDs invalid; 429 rate limit exceeded (per-IP token bucket, community
  documented max burst 300, refill 5/s — **VERIFY** exact values and policy in
  TKT-M0-03); 502/503/504 upstream failures.
- Rate limiting is **per IP** per the wiki best-practices page. **VERIFY**
  whether it is per-IP only or also per-IP+key; do not hard-code a quota
  (TKT-M0-03).
- Read-only: all endpoints in this table are GET. No write/mutation endpoint
  is used (ADR-007).

## Cache policy column

Policies are application-level decisions of this project (ADR-004 gateway),
informed by the wiki where stated. Freshness classes:

- **hot** — top-of-book / order book data; short TTL, refetched on
  dashboard refresh; drives freshness indicators (spec §3).
- **warm** — semi-static per-account data; TTL in minutes; refresh on demand
  or on a low-frequency schedule.
- **slow** — near-static reference data; long TTL / fetched once per
  session or on change.

## Endpoint table

| Endpoint | Method | Purpose in Tyrian Ledger | Required permission(s) | Batching | Expected freshness | Cache policy (ADR-004) | Authenticated |
|---|---|---|---|---|---|---|---|
| `/v2/commerce/prices` (+ `/v2/commerce/prices/{id}`, `?ids=`) | GET | Aggregated top-of-book buy/sell prices per item (`buys`/`sells` objects with `unit_price`, `quantity` in copper); candidate selection input (Journey A) | None (public) | Yes, `ids` (practical limit 200 — **VERIFY** hard limit) | hot (changes with every TP order) | Short TTL (minutes); refresh on dashboard refresh; capture time stored for freshness display | No |
| `/v2/commerce/listings` (+ `/v2/commerce/listings/{id}`, `?ids=`) | GET | Full order book per item (`buys`/`sells` arrays with `listings`, `unit_price`, `quantity` in copper); depth/price-impact simulation (M3) | None (public) | Yes, `ids` (practical limit 200 — **VERIFY** hard limit) | hot | Short TTL (minutes); fetched on demand for shortlisted candidates only, never for the whole world (rate-limit control) | No |
| `/v2/commerce/transactions/current/buys|sells`, `/v2/commerce/transactions/history/buys|sells` (paged) | GET | Pending and 90-day fulfilled TP transactions for realized-profit reconciliation (M6) | `account` + `tradingpost` (wiki: results cached server-side ~5 min) | No (path navigation + paging only; `page`/`page_size`) | warm (server-cached ~5 min per wiki; history static, current changes with pending orders) | Per-key, per-sub-endpoint cache; history pages long TTL, `current` short TTL; paging cursors stored | Yes |
| `/v2/recipes` (+ `/v2/recipes/{id}`, `?ids=`) | GET | Recipe definitions: type, output item, time_to_craft_ms, disciplines, min_rating, flags, ingredients (2022-03 schema with `type`/`id`/`count`); crafting graph core (M5) | None (public) | Yes, `ids` (practical limit 200 — **VERIFY** hard limit) | slow (changes only with game content; schema `2022-03-09T02:00:00.000Z` known to handle Currency ingredients) | Long TTL; pin schema `2022-03-09T02:00:00.000Z` (**VERIFY** this is the latest relevant version in M2) | No |
| `/v2/recipes/search?input={itemId}` / `?output={itemId}` | GET | Resolve recipe IDs for a given input ingredient or output item (crafting graph search, M5) | None (public) | No (single `input` or single `output`; mutually exclusive) | slow | Long TTL, keyed by parameter | No |
| `/v2/account/bank` | GET | Vault bank slots (item, count, charges, binding, upgrades, infusions, skins, dyes, stats); owned-item analysis | `account` + `inventories` | No (whole vault in one response) | warm (changes with user actions) | Short-to-medium TTL per refresh request; snapshot stored with capture time; never auto-poll frequently | Yes |
| `/v2/account/materials` | GET | Material storage (id, category, count; every material returned even at 0); owned-material opportunity cost (Journey B) | `account` + `inventories` | No (single response) | warm | Short-to-medium TTL; same snapshot rules as bank | Yes |
| `/v2/account/recipes` | GET | Recipe IDs unlocked for the account (recipe availability filter, M5) | `account` + `unlocks` (wiki infobox says `account, unlocks`; **VERIFY** whether `characters` is also implied) | No (single response, IDs resolved via `/v2/recipes`) | slow (changes only when recipes are learned) | Medium/long TTL | Yes |
| `/v2/characters` | GET | List character names for the account (needed to address per-character endpoints) | `account` + `characters` | No (list of names) | slow (name/character list changes rarely) | Long TTL per session; refreshed on connection | Yes |
| `/v2/characters/{name}/crafting` | GET | Per-character crafting disciplines (discipline, rating, active); crafting feasibility (Journey B, M5) | `account` + `characters` | No (one request per character) | warm (rating changes with crafting) | Medium TTL; refresh on demand / low frequency | Yes |

Notes:

- **No undocumented endpoints are used.** In particular, `v2/items` is
  referenced by the wiki inside recipe responses (e.g. `output_item_id`
  "resolvable against `/v2/items`") but is **not** in the project spec §8 list
  and is not added as a dependency here. If item metadata (names, stacks,
  rarity) is required for the UI in a later milestone, it must be proposed in
  that ticket and added to this table with its own verification
  (**VERIFY**: decide in M3/M4 whether `v2/items` is required and, if so,
  re-run the contract review for it).
- `/v2/tokeninfo` is validated by TKT-M0-04 (permission verification flow) and
  is the connection-validation endpoint; it is listed below for completeness
  because Journey B step 2 depends on it.

### `/v2/tokeninfo`

| Endpoint | Method | Purpose | Required permission(s) | Batching | Freshness | Cache policy |
|---|---|---|---|---|---|---|
| `/v2/tokeninfo` | GET | Validate the supplied API key; report `id` (first half of key only — never the full key), `name`, `permissions[]` (schema ≥ 2019-05-22 also `type`, `expires_at`, `issued_at`, `urls`); permission gating of account features | Key itself acts as credential; wiki infobox lists scope `account`; `account` permission is mandatory for all keys (wiki: API:API key) | No | slow (changes only when the key is recreated) | Very short TTL (seconds/minutes) on validation; result drives feature availability, never cached as long-lived truth |

Security notes (detailed in `docs/security/security.md`, ADR-006, TKT-M0-04):

- `tokeninfo.name` is not escaped by the API and may contain HTML/JS; it MUST
  be sanitized before any UI rendering.
- The `id` field returns only the first half of the key; the application MUST
  never log, store, or return the full key anywhere.

## MVP required permissions (summary)

| Feature set | Permissions required on the user's API key |
|---|---|
| Market scan (no key) | None |
| Account crafting + materials (M5) | `account` (mandatory for all keys) + `characters` + `inventories` + `unlocks` |
| Transaction history (M6) | additionally `tradingpost` |

Minimum-key guidance shown to the user MUST be derived from this table
(`account` is always required by the API for any authenticated endpoint).

## Fixture policy for M2

Per `docs/testing/testing-strategy.md`, fixtures are deterministic, synthetic,
small, and versioned with the tests that consume them. M2 (TKT-M2-01) requires
only the public market endpoints, so the seeded fixture categories are:

- `tests/fixtures/gw2/commerce/prices.json`
- `tests/fixtures/gw2/commerce/listings.json`

All fixtures in this directory are **synthetic**: item IDs and prices are
fictional values in integer copper; no real account, character, or key data
appears in any fixture (ADR-006).

## References

| Page | URL | Status on 2026-08-21 |
|---|---|---|
| API:2 (resource access, paging, auth, schemas, errors) | https://wiki.guildwars2.com/wiki/API:2 | fetched OK (page source) |
| API:2/commerce/prices | https://wiki.guildwars2.com/wiki/API:2/commerce/prices | fetched OK |
| API:2/commerce/listings | https://wiki.guildwars2.com/wiki/API:2/commerce/listings | fetched OK |
| API:2/commerce/transactions | https://wiki.guildwars2.com/wiki/API:2/commerce/transactions | fetched OK |
| API:2/recipes | https://wiki.guildwars2.com/wiki/API:2/recipes | fetched OK |
| API:2/recipes/search | https://wiki.guildwars2.com/wiki/API:2/recipes/search | fetched OK |
| API:2/account/bank | https://wiki.guildwars2.com/wiki/API:2/account/bank | fetched OK |
| API:2/account/materials | https://wiki.guildwars2.com/wiki/API:2/account/materials | fetched OK |
| API:2/account/recipes | https://wiki.guildwars2.com/wiki/API:2/account/recipes | fetched OK |
| API:2/characters | https://wiki.guildwars2.com/wiki/API:2/characters | fetched OK |
| API:2/characters/:id/crafting | https://wiki.guildwars2.com/wiki/API:2/characters/:id/crafting | fetched OK |
| API:2/tokeninfo | https://wiki.guildwars2.com/wiki/API:2/tokeninfo | fetched OK |
| API:Best_practices (batching ≤200 IDs, rate-limit bucket) | https://wiki.guildwars2.com/wiki/API:Best_practices | fetched OK |
| API:API key (permission definitions) | https://wiki.guildwars2.com/wiki/API:API_key | fetched OK |
| API:Terms_of_Use | https://wiki.guildwars2.com/wiki/API:Terms_of_Use | fetched OK (substantive detail owned by TKT-M0-04) |