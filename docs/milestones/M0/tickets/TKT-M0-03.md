# TKT-M0-03 - Validate API quotas, caching guidance, and error behavior

## Milestone
M0

## Goal
Turn rate-limit and external-contract assumptions into measurable configuration requirements.

## Dependencies
None

## Acceptance criteria
- [x] Document current rate-limit guidance from authoritative/community documentation.
- [x] Define application-level configurable scheduler parameters without hard-coding an unverified quota.
- [x] Document 429 handling and retry behavior.
- [x] Define which live verification is safe and how it will be performed without deliberately stressing the API.

## Required tests
- [x] A rate-limit policy document exists.
- [x] A mock 429 scenario is specified.

## Non-goals
- Load testing the real GW2 API.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.

## Decision record (executed 2026-08-22)

### Deliverables
- `docs/rate-limiting/rate-limit-policy.md` — authoritative rate-limit
  policy: documented guidance table (wiki `API:Best_practices`, `API:2`,
  `API:Terms_of_Use` fetched 2026-08-22), application-level configurable
  scheduler parameters (§2), 429 handling state (§3), mock 429 scenario
  specification (§4), and safe live-verification procedure (§5).
- `tests/fixtures/gw2/errors/429.json` — synthetic, deterministic mock 429
  fixture (no real account/key data; ADR-006) covering the §4 scenarios.
- `docs/architecture/gw2-endpoint-matrix.md` — the two "TKT-M0-03" pointers
  now reference the policy document; error bullet cites VERIFY-006.
- `docs/verification/VERIFY-REGISTER.md` — VERIFY-006 moved to IN PROGRESS
  (procedure in place, live values still unverified); VERIFY-010 (429
  response contract undocumented) and VERIFY-011 (per-IP vs per-IP+key
  empirical check) added.
- `MANIFEST.md` — new files listed.

### Method
- Wiki pages fetched 2026-08-22 via page source (`?action=raw`, HTTP 200).
  `API:Best_practices` states explicitly: rate limit is per IP, max burst 300,
  refill 5/s, 429 on exceed. These are community-documented values, not
  ArenaNet-official; the policy therefore treats them as conservative
  configurable defaults, never as hard-coded immutable facts
  (`docs/architecture/architecture.md` §Rate-limit strategy).
- 429 handling maps the architecture's 5-step policy to concrete,
  testable gateway behavior; server-provided wait info is honored when
  present and treated as optional otherwise (VERIFY-010).
- No application code exists yet (gateway lands in M1, TKT-M1-02/03);
  scheduler parameters are specified here as configuration requirements
  the M1 gateway MUST implement.

### Verification performed
- New fixture validated as well-formed JSON; no secret-like values present.
- Documentation cross-checked against the fetched wiki sources.
- No build/lint/test suite exists in the repository yet (docs-only M0 state);
  nothing executable changed.

### VERIFY items
- VERIFY-006 — IN PROGRESS: live values (per-IP vs per-IP+key, exact burst/
  refill) remain to be confirmed by the safe live check (≤2 read-only GETs,
  no burst), scheduled with M2.
- VERIFY-010 — 429 response contract (`Retry-After`/`X-RateLimit-*`/body)
  undocumented; policy treats server wait info as optional.
- VERIFY-011 — empirical sustainable rate for a single local read-only user.

### Non-goals honored
- No load testing of the real GW2 API; the live verification is bounded to
  ≤2 single read-only GETs, manually executed, never in a retry loop.
