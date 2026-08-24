You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M6.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M6-03.md

Then read dashboard/history statistics requirements relevant to this ticket.

## Mission

Complete TKT-M6-03 only.

Show useful local statistics without overstating their meaning.

Acceptance-critical work:
- show operation count, realized profit since first recorded use, justified average metrics,
  and completion rate where defined;
- show historical coverage period;
- provide empty-state messaging for insufficient history.

## Non-goals

- backfilling unknown lifetime history;
- implying results before local tracking began.

## Hard rules

- Distinguish realized from modeled/unrealized statistics.
- Explain the observation period.
- Add tests for empty, partial, and populated histories.

## Execution

1. Inspect ticket, history models, and existing dashboard/statistics code.
2. Make a maximum five-step plan.
3. Implement the smallest statistics surface.
4. Add focused tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify historical coverage is visible and insufficient history produces an explicit empty state.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
