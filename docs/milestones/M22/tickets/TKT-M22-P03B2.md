# TKT-M22-P03B2 — Bounded Request Priority and Fairness

GitHub issue: #179

M22; R2. Implementation GPT-6.1 Sol High; NORMAL independent GPT-6.1 Sol High.
Conditionally Ready after #178 merges with cache/gateway cancellation evidence.

## Outcome

Foreground action validation and account recovery reads do not sit behind an
unbounded research scan. Existing scheduler has global token/concurrency bounds,
OldestFirst queues, same-key in-flight sharing and Retry-After handling; preserve
those guards while adding bounded priorities and starvation protection.

## Contract

1. Define structured request purpose at the Application/typed-gateway boundary:
   action validation, account refresh and background research. Default unclassified
   calls conservatively to ordinary refresh, never maximum priority. Inventory
   every production caller and document its classification; React supplies none.
2. One scheduler owns outbound rate, concurrency, queue and retries. Replace only
   the necessary queuing seam; no independent per-character scheduler/timer and
   no bypass for high priority, cache miss, retries or manually requested refresh.
3. Configure finite total/per-class queue bounds and a deterministic fair policy:
   foreground can advance queued work, already-running requests are not aborted;
   at least one queued background request receives a dispatch opportunity after
   at most eight higher-priority dispatches when permits and cooldown permit.
   Validate bounds. This is local policy, not an ArenaNet quota claim.
4. Coalesced same-key work uses one queue slot/request. A foreground waiter can
   promote queued shared work without a duplicate fetch; removing that waiter
   cannot cancel surviving lower-priority waiters. Distinct private generations
   never coalesce. P03B1 completed cache hits need no scheduler permit.
5. Honor Retry-After and existing bounded attempts/timeouts. Retry waiting owns
   no active HTTP/concurrency permit; retries re-enter the same bounded/fair
   scheduler and cannot spin, monopolize permits or starve other eligible work.
   Exhausted capacity/cooldown returns typed degraded results, never financial
   fallback. Do not invent a global upstream retry scope without evidence.
6. Last-waiter cancellation releases queued work and stops retry delay. Close,
   clear/account generation and host shutdown prevent obsolete queued private
   work from starting/publishing. Preserve captured credentials and generation
   commit fences; queued cancellation alone is not authority.
7. Aggregate queue depth/wait/dispatch metrics are bounded and safe. No endpoint
   URL, actor/account/key, unbounded ID dimensions or private-body diagnostics.

## Acceptance vectors

| Case | Required result |
|---|---|
| Saturated background queue plus action preflight | Next permitted queued dispatch respects foreground priority |
| Sustained foreground arrivals, background waiting | Background dispatch by the eight-dispatch fairness bound |
| Same queued key from research then preflight | Promotion, one HTTP request, one queue slot |
| Promoting waiter cancels | Surviving shared waiter remains; no duplicate request |
| Total/per-class capacity reached | Typed bounded rejection, no overflow or permit leak |
| 429 Retry-After / repeated 5xx | No premature retry; unrelated eligible work can run; attempts bounded |
| Last waiter cancels while queued or backing off | Removed work, no later HTTP send |
| Credential switch/clear while queued | Old captured work cannot commit or serve current results |
| Public cache hit under queue saturation | Reused reference succeeds without outbound permit |
| 30 actors and overlapping scans | Existing four-read collector ceiling plus global bounds hold |

## Validation and exclusions

Deterministic barrier/virtual-delay scheduler tests, cache/gateway boundary tests,
account generation and collector fan-out tests, actual Web loop/foreground routing
classification tests, workflow suite and required CI. Record an actual bounded
load trace separately from policy tests; no upstream quota or latency guarantee.
Update scheduler architecture and only this batch row. UI unchanged unless declared
and accompanied by required visual evidence.

No research-universe/ranking change, private completed cache, native notifications,
game writes or new external dependency. P03C1 removes command-path network work;
priority alone does not satisfy F09. VERIFY-008/013/016/017 stay OPEN.
Commit `[TKT-M22-P03B2]`; PR develop, Closes #179, milestone 11.
NORMAL review and green CI before Ready; owner merges; one ticket then stop.
