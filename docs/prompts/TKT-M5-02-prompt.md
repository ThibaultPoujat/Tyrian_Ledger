You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M5.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M5-02.md

Then read account endpoint requirements, persistence architecture, and security rules relevant to this ticket.

## Mission

Complete TKT-M5-02 only.

Add bank/materials and required character data with caching and local scoping.

Acceptance-critical work:
- fetch only endpoints required for enabled account features;
- cache account snapshots separately from public market data;
- associate snapshots with a local account profile identifier;
- handle missing permissions and partial data gracefully.

## Non-goals

- persisting unnecessary raw payloads indefinitely;
- fetching every account endpoint by default;
- account data cloud sync.

## Hard rules

- Use only verified endpoints/fields.
- Never store credential/token values in account snapshots.
- Preserve read-only gateway and request minimization.
- Add tests for permission gating, caching, scoping, and partial-data handling.

## Execution

1. Inspect ticket, endpoint matrix, account models, and persistence architecture.
2. Make a maximum five-step plan.
3. Implement only required account snapshot paths.
4. Add focused tests.
5. Run narrow tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic account fixtures. Confirm only enabled features trigger account requests and that
missing permissions do not crash the application.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
