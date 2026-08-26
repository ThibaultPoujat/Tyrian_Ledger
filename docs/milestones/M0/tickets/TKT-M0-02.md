# TKT-M0-02 - Validate GW2 API endpoint and permission matrix

## Milestone
M0

## Goal
Create the authoritative endpoint table used by the application.

## Dependencies
None

## Acceptance criteria
- [x] List every MVP endpoint, purpose, required permission, batching capability, expected freshness, and cache policy.
- [x] Mark every uncertain fact VERIFY.
- [x] Include prices, listings, recipes/search, account tokeninfo, bank/materials, character crafting/recipes, and any other endpoint actually required.
- [x] No undocumented endpoint is added as a dependency.

## Required tests
- [x] Review table against current documentation URLs.
- [x] At least one saved response fixture exists for each endpoint category later required by M2.

## Non-goals
- Implementing the HTTP client.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Decision record (executed 2026-08-21)

### Deliverables
- `docs/architecture/gw2-endpoint-matrix.md` — authoritative endpoint table:
  global request rules, per-endpoint purpose / permission / batching /
  freshness / cache policy, MVP permission summary, fixture policy, and a
  reference table with fetch status.
- `tests/fixtures/gw2/commerce/prices.json` and
  `tests/fixtures/gw2/commerce/listings.json` — the two endpoint categories
  required by M2 (TKT-M2-01: public market data only).
- VERIFY register updated with VERIFY-004..VERIFY-008.
- `MANIFEST.md` updated.

### Method
- All 15 referenced wiki pages fetched 2026-08-21 via the Guild Wars 2 Wiki
  page source (`?action=raw`, HTTP 200 for every page) and the rendered
  pages. Facts the wiki states explicitly are recorded as-is; every fact the
  wiki does not pin down is marked VERIFY in the matrix and registered.
- Quota/rate-limit policy and legal scope are left to TKT-M0-03/TKT-M0-04 to
  avoid duplicating those tickets' work; the matrix only records the
  community-documented values with VERIFY.

### Endpoints in the table
`/v2/commerce/prices`, `/v2/commerce/listings`,
`/v2/commerce/transactions/{current|history}/{buys|sells}`, `/v2/recipes`,
`/v2/recipes/search`, `/v2/account/bank`, `/v2/account/materials`,
`/v2/account/recipes`, `/v2/characters`, `/v2/characters/{name}/crafting`,
`/v2/tokeninfo`. Exactly the set of `docs/specs/project-spec.md` §8 plus
`/v2/characters` (required to address per-character endpoints). No other
endpoint is a dependency; `v2/items` is explicitly noted as NOT used (VERIFY-007).

### Fixes to existing documents
- `docs/specs/verified-external-notes.md` listed
  `https://wiki.guildwars2.com/wiki/API_key` (redirect page, 25 bytes); the
  canonical page is `https://wiki.guildwars2.com/wiki/API:API_key` — updated.

### Fixture note
Fixtures are synthetic (fictional item IDs, integer-copper prices, no real
account/character/key data — ADR-006) and are the only M0 deliverables under
`tests/`; reusable fixture conventions remain TKT-M1-03's scope.

### Verification performed
- Fixtures validated as well-formed JSON and checked for absence of any
  secret-like values (no key strings, no real account data).
- Table cross-checked field-by-field against the fetched page sources.
- No application code or tests exist yet (M1 not started), so there is no
  build/lint suite to run; no behavioral code changed.

### VERIFY items (registered)
- VERIFY-004 — exact `ids` batch hard limit / 206 / paging details.
- VERIFY-005 — schema version to pin per endpoint (check `/v2.json?v=latest` in M2).
- VERIFY-006 — rate-limit granularity/values (owned by TKT-M0-03).
- VERIFY-007 — whether `v2/items` is required by a later milestone.
- VERIFY-008 — `account/recipes` scope detail and transactions server-cache duration.

### Follow-up
TKT-M0-03 (quota/rate-limit policy document) and TKT-M0-04 (permission and
legal scope) build on this table; TKT-M1-03 adopts the fixture directory
convention; TKT-M2-01 consumes the seeded fixtures.
