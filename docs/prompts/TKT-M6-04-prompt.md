You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M6.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M6-04.md

Then read local-data management, security, and backup guidance relevant to this ticket.

## Mission

Complete TKT-M6-04 only.

Give the user control over local account/history data.

Acceptance-critical work:
- provide a user-visible clear-account-data action;
- distinguish account snapshots from public cache/history where sensible;
- document SQLite backup/restore at user level;
- ensure data is not silently uploaded.

## Non-goals

- automated cloud backup;
- remote user accounts;
- deleting data outside the application's documented local scope.

## Hard rules

- Make destructive actions explicit and confirmable.
- Preserve local-first behavior.
- Do not delete public market history when only account data is requested unless the ticket
  explicitly defines that scope.
- Add tests for clear-data boundaries and backup/restore guidance where applicable.

## Execution

1. Inspect ticket, persistence model, and local-data security guidance.
2. Make a maximum five-step plan.
3. Implement the smallest safe data-management surface.
4. Add focused tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify clear-data scope, confirmation behavior, and local-only messaging. Confirm no network
upload path is introduced.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
