You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M2.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M2-03.md

Then read the architecture and cache policy documents required by this ticket.

## Mission

Complete TKT-M2-03 only.

Cache public market responses and expose data age.

Acceptance-critical work:
- cache entries include capture time and expiry policy;
- cache hits avoid network requests;
- refresh/invalidation is deterministic;
- data freshness is available to analytics and UI.

## Non-goals

- historical market database;
- account data caching unless explicitly required by the ticket;
- unrelated cache infrastructure.

## Hard rules

- Preserve the single gateway and rate-management architecture.
- Do not hide stale data; expose freshness state.
- Do not invent endpoint freshness requirements.
- Add deterministic tests for cache hit/miss, expiry, refresh, and data age.

## Execution

1. Inspect ticket, current gateway/cache code, and relevant policies.
2. Make a maximum five-step plan.
3. Implement the smallest cache mechanism.
4. Add focused tests using a deterministic clock.
5. Run narrow tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts at an operation, report the
blocker and stop.

## Validation

Confirm cache hits avoid the network, expiry is deterministic, and freshness metadata survives
the path needed by analytics/UI. No live API is required for normal tests.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
