# TKT-M22-P03B1 — Bounded Public Reference Cache

GitHub issue: #178

M22; R2. Implementation GPT-6.1 Sol High; NORMAL independent GPT-6.1 Sol High.
Conditionally Ready after C04/#177 merges with review and final-head CI.

## Outcome and entry evidence

Reuse item metadata and recipe definitions across sequential analysis and holdings
refreshes without repeated outbound reference reads. Existing Gw2RequestScheduler
only coalesces in-flight requests; a completed request is fetched again. B2's
collector/projection and P03A fence must remain intact. Inspect actual gateways,
pinned schema and tests at entry; do not infer endpoint semantics from elapsed time.

## Contract

1. Add one bounded Infrastructure cache behind existing typed public gateway
   methods for item metadata, recipe definitions and the public recipe-ID universe
   where the existing gateway supports it. Key by endpoint/schema/language and
   normalized IDs. Equivalent ID order shares data; distinct schema/language does
   not. Reuse per-ID data across overlapping bounded batches without extra fan-out.
2. Use explicit configurable local TTL and size bounds with an injectable clock,
   validated positive limits and deterministic expiry/eviction tests. These are
   application reuse policies, never assertions about ArenaNet update schedules.
   Process-memory storage is sufficient; persistence/retention changes are excluded.
3. Cache only fully validated successful reference entries. A malformed, missing,
   failed or partial batch cannot fabricate a cached complete set. Previously
   valid unexpired entries may be used; missing requested IDs retain the gateway's
   honest partial/failure semantics. Expired references needed for category,
   protection or recipe feasibility cannot silently become fresh eligibility.
4. Retain in-flight coalescing, bounded batches, cancellation ownership and retries.
   Cache hits avoid outbound permits. One cancelled waiter cannot cancel others;
   failed/cancelled fills are removable and a subsequent caller can retry.
5. Return immutable/detached results; a caller cannot corrupt later readers.
   Store no API key, private roster, actor, holdings, wallet, TP or account unlocks.
   Private generation isolation remains P03A's authority. Public reuse across
   accounts confers no private capability or physical freshness.
6. Commerce prices/listings and every private endpoint retain their current
   freshness/read policy. No completed private-response cache is authorized while
   VERIFY-008/016/017 remain unresolved. No stale-while-revalidate action admission.
7. Wire real holdings metadata and crafting/reference discovery consumers through
   this same gateway seam. Expose bounded aggregate hit/miss/eviction diagnostics
   without URLs/query/private IDs or secrets. No new timer or broad scheduler rewrite.

## Acceptance vectors

| Case | Required result |
|---|---|
| Two sequential same-item metadata reads | One outbound read before local expiry |
| IDs 1,2 then 2,3 | ID 2 reused; ID 3 fetched in a bounded request; complete result honest |
| Reordered/duplicated IDs | Same normalized identity; deterministic result |
| Different schema/language/endpoint | No incorrect cross-key reuse |
| TTL boundary or backward clock | Expired/invalid-age entry never counts as fresh |
| Cache capacity exceeded | Bounded retained entries and deterministic replacement |
| One of two waiters cancels | Remaining waiter succeeds; no poisoned fill |
| Partial/404/malformed batch then recovery | No phantom complete cache; later valid read works |
| Caller mutates returned collection | Later cache read remains unchanged |
| Account A→B, clear/restore | Public references reusable; private data/eligibility not reused |
| Holdings/category and crafting repeated reads | Actual consumer boundaries demonstrate fewer reads and identical safe results |

## Validation and exclusions

New gateway/cache tests with independent request-count/clock vectors; existing
Gw2RequestScheduler/Gw2ApiClient/Gw2GatewayBoundary and AccountHoldings gateway/
snapshot tests; Application Crafting/holdings policy regressions; workflow tests
and required CI. Update architecture cache ownership/policy and only this batch row.
Run frontend tests/build only if payloads change; UI changes require original/actual
1920×1080 evidence. Expected scope API/Infrastructure only.

No private-response or market-action cache, database table, freshness guarantee,
fee/category redesign, timer, paid service or production dependency change.
P03B2 owns priority; P03C owns decision snapshots. VERIFY-008/013/016/017 stay OPEN.
Commit `[TKT-M22-P03B1]`; PR develop, Closes #178, actual milestone 11.
Missing validation/review means Draft. Owner merges; stop after this ticket.
