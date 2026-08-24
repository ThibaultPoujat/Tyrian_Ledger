You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M3.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M3-02.md

Then read the market-depth models and verified commerce schemas required by this ticket.

## Mission

Complete TKT-M3-02 only.

Calculate acquisition and liquidation scenarios across order-book levels.

Acceptance-critical work:
- simulate quantity across multiple buy/sell levels;
- calculate weighted average execution price and price impact;
- handle insufficient depth explicitly;
- return transparent assumptions and remaining quantity.

## Non-goals

- guaranteeing real-world fills;
- order execution or Trading Post automation.

## Hard rules

- Use integer copper.
- Never invent order-book fields.
- Treat modeled depth as a scenario, not an execution guarantee.
- Add deterministic tests for full depth, partial depth, zero/invalid quantity, and price impact.

## Execution

1. Inspect ticket, commerce models, and current analytics structure.
2. Make a maximum five-step plan.
3. Implement the smallest pure calculation service.
4. Add focused unit tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic fixtures only. Confirm the output explains assumptions and remaining quantity.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
