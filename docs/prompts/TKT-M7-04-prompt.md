You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M7.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M7-04.md

Then read historical-opportunity and UX requirements relevant to this ticket.

## Mission

Complete TKT-M7-04 only.

Present long-term observations as evidence, not certainty.

Acceptance-critical work:
- show historical range/percentile and liquidity context;
- show data coverage and sample count;
- use observed/estimated language rather than guarantees;
- allow comparison of a small watchlist.

## Non-goals

- automated investment advice presented as fact;
- future-price prediction.

## Hard rules

- Historical displays must expose observation window and sample size.
- Do not overstate thin data.
- Keep read-only behavior.
- Add focused UI tests for watchlist comparison and insufficient-data states.

## Execution

1. Inspect ticket, historical metrics, opportunity models, and UX rules.
2. Make a maximum five-step plan.
3. Implement the smallest historical-opportunity UI/model change.
4. Add focused tests.
5. Run tests/browser checks and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify coverage/sample metadata and careful wording. Confirm the UI does not present historical
observations as guaranteed future returns.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
