# VERIFY Register

Central register of unresolved or time-sensitive verification items for
Tyrian Ledger.

## Purpose

This file tracks facts, assumptions, external-contract questions, and other
items that must be verified before they can be treated as project facts.

It is a project-level index only.

The ticket, ADR, specification, or other authoritative project document where
the item originated remains the primary source of context and evidence.

## Rules

1. Never invent or silently resolve a VERIFY item.
2. A VERIFY item may only be marked RESOLVED when supporting evidence has been
   recorded in the relevant ticket, ADR, specification, or other authoritative
   project document.
3. When resolving an item, record:
   - what was verified,
   - the evidence/source,
   - the date,
   - the ticket that performed the verification.
4. If verification changes an architectural or durable cross-cutting decision,
   follow the project's ADR policy.
5. Do not delete resolved items. Mark them RESOLVED so the project retains an
   audit trail.
6. New VERIFY items discovered during a ticket must be added to this register
   before the ticket is considered complete.
7. A ticket must not claim completion while introducing an unresolved VERIFY
   item that belongs to the ticket's acceptance criteria.
8. VERIFY items concerning external APIs, quotas, permissions, legal
   constraints, security requirements, or other time-sensitive facts should
   include an authoritative source where available.
9. When an existing VERIFY item is affected by new evidence, update its status
   and evidence rather than creating a duplicate entry.
10. The register must remain concise. Detailed investigation belongs in the
    relevant ticket or project document.

## Status values

- OPEN — verification is still required.
- IN PROGRESS — verification is actively being investigated.
- RESOLVED — sufficient evidence has been recorded.
- REJECTED — the assumption/question was determined not to apply.
- SUPERSEDED — replaced by a newer verification or decision; link the
  replacement.

## Register

| ID | Status | Description | Source / Evidence | Owner Ticket | Date Added | Date Resolved |
|---|---|---|---|---|---|---|
| VERIFY-001 | SUPERSEDED | Upstream license terms of the retired Qwen/MTPLX development-model path. No longer relevant to active development after the Codex migration. | Historical evidence: `docs/milestones/M0/tickets/TKT-M0-01.md`; superseded by TKT-M1-00 | TKT-M0-01 | 2026-08-21 | 2026-08-26 |
| VERIFY-002 | SUPERSEDED | Hardware fit of the retired MTPLX artifact. No longer relevant to active development after the Codex migration. | Historical evidence: `docs/milestones/M0/tickets/TKT-M0-01.md`; superseded by TKT-M1-00 | TKT-M0-01 | 2026-08-21 | 2026-08-26 |
| VERIFY-003 | SUPERSEDED | Live smoke test for the retired MTPLX development path. No longer relevant to active development after the Codex migration. | Historical evidence: `docs/milestones/M0/tickets/TKT-M0-01.md`; superseded by TKT-M1-00 | TKT-M0-01 | 2026-08-21 | 2026-08-26 |
| VERIFY-004 | OPEN | Exact hard limit of the `ids` batch parameter (community best-practices page documents 200 IDs per request) and exact 206 / paging behavior (max `page_size`, per-endpoint applicability). | `docs/architecture/gw2-endpoint-matrix.md` — global rules; wiki `API:Best_practices` and `API:2` fetched 2026-08-21. TKT-M2-01's 2026-08-26 no-key probe observed that unparameterized `/commerce/prices?v=latest` returns a numeric ID index (27,987 entries); it did not exercise batches or 206 behavior. TKT-M9-02 (2026-08-31) uses deterministic batches of at most 200 and rejects HTTP 206 / response-ID mismatches as incomplete input; exact hard limits and paging still require separate evidence. | TKT-M0-02 | 2026-08-21 | — |
| VERIFY-005 | OPEN | Schema version to pin per endpoint via `?v=` / `X-Schema-Version`; the latest relevant version per endpoint must be confirmed against `/v2.json?v=latest` (matrix recipes row assumes schema `2022-03-09T02:00:00.000Z`). | `docs/architecture/gw2-endpoint-matrix.md`; wiki `API:2` fetched 2026-08-21. TKT-M2-01's 2026-08-26 no-key `/v2.json?v=latest` probe established `2025-08-29T01:00:00.000Z` as the current global version. TKT-M9-02 rechecked that same newest global entry on 2026-08-31 and pins it for public prices, listings, and items; other endpoint-specific verification remains open. | TKT-M0-02 | 2026-08-21 | — |
| VERIFY-006 | IN PROGRESS | Whether the GW2 API rate limit (per-IP token bucket; community-documented burst 300, refill 5/s) is applied per IP only or per IP+key, and the exact values. Guidance re-fetched 2026-08-22 and converted into configurable scheduler parameters + a safe live-verification procedure; values remain community-estimates until the M2 live check. | wiki `API:Best_practices` fetched 2026-08-21 and 2026-08-22; `docs/rate-limiting/rate-limit-policy.md` §1/§5. TKT-M2-01's 2026-08-26 no-key probe observed `X-Rate-Limit-Limit: 600` on two HTTP 200 responses. TKT-M9-02's 2026-08-31 public `/v2/items` and `/v2.json` checks observed the same header on HTTP 200 responses. This does not prove limiter scope, burst/refill behavior, or a scheduler default. | TKT-M0-03 | 2026-08-21 | — |
| VERIFY-007 | RESOLVED | Public `/v2/items` supplies M9 finalist display metadata. Its per-item normal stack cap is intentionally product policy, not API data. | TKT-M9-02, `docs/specs/verified-external-notes.md` (2026-08-31): keyless `v2.json?v=latest` marks `/v2/items` active and unauthenticated; a public `ids` response supplied `id`/`name` but no `max_stack`. The endpoint matrix records the `lang=en`, pinned-schema client contract. Shared `ids` batching, 206, paging, and schema-version questions remain under VERIFY-004/005. | TKT-M9-02 | 2026-08-21 | 2026-08-31 |
| VERIFY-008 | OPEN | `/v2/account/recipes` scope is `account, unlocks` per the wiki infobox; confirm whether the `characters` scope also affects the unlocked recipe set, and confirm the exact server-side caching duration of `/v2/commerce/transactions` (wiki cites ~5 minutes). | wiki `API:2/account/recipes`, `API:2/commerce/transactions` fetched 2026-08-21 | TKT-M0-02 | 2026-08-21 | — |
| VERIFY-010 | OPEN | Exact 429 response contract: whether the API sends `Retry-After` (and/or `X-RateLimit-*` headers) and a machine-readable body; the wiki documents neither, so the policy treats server-provided wait info as optional. | `docs/rate-limiting/rate-limit-policy.md` §1; wiki `API:2` fetched 2026-08-22 | TKT-M0-03 | 2026-08-22 | — |
| VERIFY-011 | OPEN | Empirical per-IP vs per-IP+key rate-limit behavior and the sustainable request rate for a single local read-only user; to be settled by the safe live check (≤2 read-only GETs, no burst) defined in the policy doc, scheduled with M2. | `docs/rate-limiting/rate-limit-policy.md` §5 | TKT-M0-03 | 2026-08-22 | — |
| VERIFY-012 | OPEN | A verified response-level discriminator, if any, for the community-observed transient "Invalid key" 403 condition. Until one exists, the gateway must treat all 401/403 responses as permanent and must not retry them. | `docs/rate-limiting/rate-limit-policy.md` §1/§3; TKT-M2-02 decision, 2026-08-27 | TKT-M2-02 | 2026-08-27 | — |
| VERIFY-013 | OPEN | Exact Guild Wars 2 Trading Post fee contract for a sale: M9 uses a 5% listing fee plus 10% exchange fee, each with a 1-copper minimum and owner-approved per-fee round-up policy. Exact fractional-copper rounding remains externally unverified, so modeled profit is not authoritative. | [Trading Post — Guild Wars 2 Wiki](https://wiki.guildwars2.com/wiki/Trading_Post), reviewed 2026-08-31, documents the separate 5%/10% fees and a 1c minimum for each but does not state fractional-copper rounding. TKT-M9-03 records the owner-selected provisional round-up policy; authoritative external rounding evidence remains required. | TKT-M9-03 | 2026-08-31 | — |
| VERIFY-014 | OPEN | M10 scheduled capture and static deployment evidence is pending: record trusted workflow evidence for the 15-minute capture, configured request/concurrency/burst limits, selector validation and `develop` fallback, static-artifact contents, and the pre-publication audit before the owner makes the repository or Pages site public. | ADR-008 and TKT-M10-05 define the required evidence. This is a deployment-evidence item, not confirmation of external Guild Wars 2 API behaviour; batching, schema, rate-limit, retry, and fee facts remain under VERIFY-004, -005, -006, -010, -011, and -013. | TKT-M10-05 | 2026-09-01 | — |

## Verification history

Resolved items remain in the table above.

For significant verification decisions, detailed evidence should remain in the
ticket or other authoritative project document rather than being duplicated
here.

## Open items

| Item | Ticket | Status |
| --- | --- | --- |
| M1-01 skeleton is verified locally; the CI workflow now exists but its GitHub run should be confirmed before M1 closes. | TKT-M1-01 | Open |
| E2E stub (`tests/Gw2Tp.Web.E2E`) had no tests; an offline Playwright smoke suite now verifies both the local API health endpoint and React shell. | TKT-M1-03 | RESOLVED — 2026-08-26 |
| Placeholder C# test projects (`Gw2Tp.Domain.Tests`, `Gw2Tp.Application.Tests`, `Gw2Tp.Analytics.Tests`, `Gw2Tp.Infrastructure.Tests`) had no tests; each active layer now has executable harness coverage. | TKT-M1-03 | RESOLVED — 2026-08-26 |
