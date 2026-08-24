You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M5.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M5-03.md

Then read opportunity-cost and account-item rules relevant to this ticket.

## Mission

Complete TKT-M5-03 only.

Value owned materials economically and compare owned, bought, and mixed strategies.

Acceptance-critical work:
- never assume owned materials are free;
- compute realizable economic value from configured market evidence;
- support owned/buy/mixed strategies when data permits;
- flag bound, unavailable, or non-sellable items where verified data supports that distinction.

## Non-goals

- assuming every inventory item is sellable;
- changing account data semantics unrelated to opportunity cost.

## Hard rules

- Use integer copper.
- Make opportunity-cost assumptions explicit.
- Do not invent item/account fields.
- Add deterministic tests for owned/buy/mixed paths and unavailable data.

## Execution

1. Inspect ticket, account item models, market valuation, and financial rules.
2. Make a maximum five-step plan.
3. Implement the smallest valuation service.
4. Add focused unit tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic account/market fixtures. Confirm an owned item has an opportunity cost rather than
zero cost merely because it is already owned.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
