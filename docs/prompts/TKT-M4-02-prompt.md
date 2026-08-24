You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M4.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M4-02.md

Then read UX and opportunity-calculation documents relevant to this ticket.

## Mission

Complete TKT-M4-02 only.

Expose calculation details and assumptions.

Acceptance-critical work:
- show acquisition/exit assumptions, fees, modeled profit, ROI, capital, order-book impact,
  liquidity, and data age;
- provide a human-readable calculation breakdown;
- distinguish scenario values from actual outcomes.

## Non-goals

- executing actions from the detail page;
- changing backend formulas solely for presentation.

## Hard rules

- Present backend calculation evidence without inventing values.
- Make assumptions and freshness visible.
- Preserve read-only behavior.
- Add focused UI tests for calculation-detail rendering and state handling.

## Execution

1. Inspect ticket, UX rules, and existing opportunity/detail models.
2. Make a maximum five-step plan.
3. Implement the smallest detail view.
4. Add focused tests.
5. Run narrow tests/browser checks and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify scenario-versus-realized wording and that all displayed values come from the approved
model/view data.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
