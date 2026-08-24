You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M6.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M6-02.md

Then read profit/reconciliation and operation-history specifications relevant to this ticket.

## Mission

Complete TKT-M6-02 only.

Calculate realized profit separately from unrealized P/L.

Acceptance-critical work:
- realized profit uses recorded actual acquisition/sale values and applicable fees;
- unrealized value is separately labeled;
- incomplete operations are not silently counted as realized profit;
- statistics are reproducible from stored records.

## Non-goals

- claiming lifetime profit before local history begins;
- replacing recorded facts with current market estimates.

## Hard rules

- Use integer copper and deterministic formulas.
- Keep realized and modeled/unrealized values distinct in domain models and UI contracts.
- Add unit tests for completed, partial, cancelled, and unrealized cases.

## Execution

1. Inspect ticket, operation records, and financial rules.
2. Make a maximum five-step plan.
3. Implement the smallest reconciliation/calculation change.
4. Add focused tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify profit statistics can be recomputed from stored records and that incomplete operations are
excluded from realized totals.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
