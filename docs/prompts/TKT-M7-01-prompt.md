You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M7.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M7-01.md

Then read the historical-data specification and local persistence schema relevant to this ticket.

## Mission

Complete TKT-M7-01 only.

Create the foundation for historical data without over-collecting.

Acceptance-critical work:
- store timestamped price/order-book snapshots with source freshness;
- define sampling policy by item class/watchlist;
- estimate local storage growth;
- allow future schema evolution without corrupting existing history.

## Non-goals

- full-frequency capture of every item;
- live collection of large datasets before policy is validated.

## Hard rules

- Keep historical collection bounded and API-efficient.
- Do not invent source fields or freshness semantics.
- Make retention/sampling assumptions explicit.
- Add tests for schema mapping, retention/sampling decisions, and storage calculations.

## Execution

1. Inspect ticket, historical-data requirements, and persistence architecture.
2. Make a maximum five-step plan.
3. Implement the smallest history model/schema/policy.
4. Add focused tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic snapshots. Confirm storage estimates and schema behavior are deterministic and
that no unbounded collection is introduced.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
