You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M8.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M8-02.md

Then read UX/accessibility requirements and the documented browser matrix relevant to this ticket.

## Mission

Complete TKT-M8-02 only.

Validate the desktop UI across target browsers and accessibility basics.

Acceptance-critical work:
- keyboard navigation works for primary flows;
- focus states are visible;
- critical information meets the chosen contrast standard;
- loading/error/empty states are understandable;
- smoke tests run against the documented browser matrix.

## Non-goals

- mobile-first redesign;
- unrelated visual redesign.

## Hard rules

- Follow the project UX guidance rather than inventing a new design system.
- Preserve read-only behavior.
- Add focused browser/accessibility tests for critical flows.
- Report unsupported browser behavior as a limitation rather than masking it.

## Execution

1. Inspect ticket, UX guidance, and browser-test setup.
2. Make a maximum five-step plan.
3. Implement only ticket-scoped fixes.
4. Run focused browser/accessibility checks.
5. Inspect the diff and acceptance criteria.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Run the documented browser matrix or the closest supported subset and record exactly what was
validated.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
