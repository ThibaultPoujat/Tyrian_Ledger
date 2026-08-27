# GW2 API Rate-Limit and Retry Policy

Authoritative rate-limit, 429-handling, and safe-live-verification policy for
Tyrian Ledger. Implements ADR-004 (single GW2 API gateway) and the
"Rate-limit strategy" section of `docs/architecture/architecture.md`.

Every GW2 request in this application MUST be scheduled through the gateway
(`IGw2ApiClient`) using the parameters below. Feature code must never bypass
the scheduler and must never hard-code a quota as an immutable fact.

## 1. Documented guidance (authoritative / community sources)

Fetched **2026-08-22** from the Guild Wars 2 Wiki (page source, HTTP 200).

| Fact | Value | Source | Status |
|---|---|---|---|
| Rate limiting applied per IP | per IP (not documented per key) | `API:Best_practices` §Rate Limit, 2026-08-22 | documented; **VERIFY-006** whether also per IP+key |
| Max burst size (bucket size) | 300 | `API:Best_practices` §Rate Limit | community-documented, not ArenaNet-official; **VERIFY-006** for exact value |
| Refill rate | 5 tokens/s (300/min) | `API:Best_practices` §Rate Limit | community-documented; **VERIFY-006** |
| 429 semantics | returned when the rate limit was exceeded | `API:2` §Error codes, 2026-08-22 | documented |
| 429 response body/headers (`Retry-After`, `X-RateLimit-*`) | not documented | wiki does not state them | **VERIFY-010** |
| Community observation for random "Invalid key" 403s | retry later instead of marking key invalid | `API:Best_practices` §Invalid API-Keys, 2026-08-22 | insufficient to classify an individual response; **VERIFY-012** |
| Legal constraint | API use is subject to ArenaNet Content/Website Terms of Use | `API:Terms_of_Use`, 2026-08-22 | authoritative (thin pointer page); legal scope is TKT-M0-04 |

Note: the wiki is the community's best documentation of the live API; it is
not ArenaNet official. The exact contract MUST be revalidated before release
(`docs/specs/verified-external-notes.md`).

### M2 public-probe evidence

TKT-M2-01 performed the approved bounded, no-key check on 2026-08-26: one
`/v2.json?v=latest` request and one `/v2/commerce/prices?v=latest` request.
Both returned HTTP 200 with `X-Rate-Limit-Limit: 600`. This is an observed
response header only: it does not establish the limiter scope, burst/refill
behavior, 429 contract, or sustainable rate, and therefore does not change
the configurable scheduler defaults in §2. See VERIFY-006, VERIFY-010, and
VERIFY-011.

## 2. Application-level scheduler parameters (configurable)

These parameters configure the gateway's request scheduler (ADR-004). They are
application configuration, NOT asserted API facts: defaults below are set
conservatively at or below the community-documented values and must remain
adjustable. No parameter may hard-code an unverified quota as the only
possible value.

| Parameter | Meaning | Default | Constraint / note |
|---|---|---|---|
| `Gw2Api:RateLimit:BurstSize` | Token-bucket capacity | 300 | ≤ community-documented burst (VERIFY-006); must be user-adjustable |
| `Gw2Api:RateLimit:RefillTokensPerSecond` | Token-bucket refill rate | 5 | ≤ community-documented refill (VERIFY-006) |
| `Gw2Api:RateLimit:MaxConcurrentRequests` | In-flight requests cap | 5 (VERIFY-011 tuning) | Deduplication must collapse identical in-flight requests (architecture.md) |
| `Gw2Api:Retry:On429:InitialBackoffMs` | First backoff after a 429 | 1000 | Bounded exponential backoff (architecture.md) |
| `Gw2Api:Retry:On429:MaxBackoffMs` | Computed backoff ceiling | 30000 | Does not override a valid server `Retry-After` |
| `Gw2Api:Retry:On429:MaxAttempts` | Total outbound attempts per logical request, including the initial request | 5 | Beyond this: surface `RateLimited` error state, do not block the UI |
| `Gw2Api:Retry:HonorServerRetryAfter` | Use server-provided wait if present | true | Only when the header exists (VERIFY-010); it takes precedence over the computed backoff |
| `Gw2Api:Retry:On5xx:InitialBackoffMs` / `MaxBackoffMs` / `MaxAttempts` | Retry for 502/503/504 | 1000 / 30000 / 3 | Same bounded pattern; `MaxAttempts` includes the initial request |
| `Gw2Api:RequestTimeoutMs` | Per-request timeout | 10000 | |

Defaults are starting points chosen for a single local, read-only user; the
real sustainable rate is whatever the live API confirms (VERIFY-006,
VERIFY-011). After the safe live check in M2, defaults MUST be re-examined and
recorded in this file.

## 3. 429 handling and retry behavior

Maps the architecture.md 5-step policy to concrete gateway behavior:

1. **Record the event** — increment the 429 metric (architecture.md
   Observability) and log at warning level WITHOUT any key material (ADR-006).
2. **Respect server-provided retry information when available** — if the 429
   carries `Retry-After` (or equivalent, VERIFY-010), wait that duration; it
   overrides computed backoff.
3. **Bounded exponential backoff** — otherwise wait
   `initialBackoffMs * 2^(attempt-1)`, capped at `maxBackoffMs`, per the
   parameters in §2.
4. **Suppress duplicate concurrent requests** — while a request is in
   backoff, identical (same endpoint + parameters + account scope) requests
   are deduplicated, never re-fired in parallel.
5. **Surface a useful UI state** — map to the `RateLimited` error category
   (architecture.md Error taxonomy) with a human-readable message and, where
   known, the expected wait; the app must never spin silently or hammer the
   API.

M2 treats all 401 and 403 responses as permanent: it returns their stable
error category without retrying. The public endpoint response contract does
not provide a verified discriminator for the community-observed random
"Invalid key" 403 case; **VERIFY-012** records the evidence needed before a
future authenticated gateway can introduce a narrow exception.

## 4. Mock 429 scenario specification

Reference fixture: `tests/fixtures/gw2/errors/429.json`. It is synthetic,
deterministic, and contains no real account, character, or key data
(ADR-006; `docs/testing/testing-strategy.md`).

The M1/M2 gateway tests MUST cover at least:

- `429-with-retry-after`: response carries `Retry-After`; the scheduler waits
  the server value, retries, and succeeds on the next attempt.
- `429-bare`: response carries no retry header; the scheduler applies bounded
  exponential backoff and succeeds within `retry.on429.maxAttempts`.
- `429-exhausted`: persistent 429s beyond `maxAttempts`; the gateway surfaces
  `RateLimited` and the deduplicator has not spawned duplicate concurrent
  requests (asserted on the request count sent to the mock transport).
- `429-while-deduplicated`: a second identical request issued during the
  backoff window is coalesced with the first; only the original 429 and its
  retry reach the mock transport (two sends, not a parallel duplicate).

The fixture is a specification artifact; the deterministic mock transport and
scheduler tests are implemented by TKT-M2-02.

## 5. Safe live verification (non-stressing)

Allowed verification, performed from this machine, read-only, bounded:

1. **Public, no-key probe** — a single
   `GET https://api.guildwars2.com/v2/commerce/prices` (no authentication).
   One request only. Record: status, all `X-` headers, body shape, and timing.
   This probes the rate-limit headers WITHOUT any key and cannot stress
   anything (1 request << any plausible bucket).
2. **Keyed identity probe** — one `GET /v2/tokeninfo` with the user's local
   API key (header from the local secret store; never logged or committed).
   One request only. Resolves the key's permissions and lets us compare
   per-IP vs per-IP+key behavior only if step 1's headers distinguish them.

Rules:

- Read-only GETs on endpoints already listed in
  `docs/architecture/gw2-endpoint-matrix.md` only (ADR-007).
- No deliberate burst, no repeated 429 triggering, no load testing
  (explicit ticket non-goal). A 429 that still occurs is observed, recorded,
  and treated as evidence for VERIFY-006/VERIFY-011 — never "fixed" by
  retrying in a tight loop.
- Total live requests for this verification: ≤ 2, spaced, manually executed.
- Record results (headers, status, timestamps) in
  `docs/specs/verified-external-notes.md` and update VERIFY-006 / VERIFY-010 /
  VERIFY-011 in `docs/verification/VERIFY-REGISTER.md`.

Execution timing: the safe live check is scheduled with the M2 gateway work
(when a secret store and real key are available) so its results can
immediately adjust the §2 defaults. It may be run earlier as step 1 only
(no key involved) if the human approves.
