You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M7.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M7-03.md

Then read historical-metrics and charting requirements relevant to this ticket.

## Mission

Complete TKT-M7-03 only.

Compute historical metrics from local observations without implying future prediction.

Acceptance-critical work:
- compute percentiles, volatility, drawdown, spread persistence, and liquidity stability where specified;
- require sufficient samples before presenting statistics as meaningful;
- disclose observation window and sample count;
- use only locally available observations.

## Non-goals

- machine-learning price forecasts;
- claims about future market direction.

## Hard rules

- Metrics must be deterministic.
- Missing/insufficient data must be explicit.
- Do not invent statistical definitions; use the project specification or VERIFY.
- Add unit tests for metric formulas, insufficient samples, and edge cases.

## Execution

1. Inspect ticket, historical models, and metric specification.
2. Make a maximum five-step plan.
3. Implement the smallest pure metric services.
4. Add focused tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic local observations. Verify sample counts/window metadata accompany metrics and no
future prediction is implied.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
