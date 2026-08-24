You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M3.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M3-01.md

Then read ADR-005 and the financial rules required by this ticket.

## Mission

Complete TKT-M3-01 only.

Create exact copper arithmetic and a centralized transaction-fee policy.

Acceptance-critical work:
- represent money as integer copper;
- use no floating-point arithmetic for money calculations;
- isolate the fee policy and keep it configurable where required;
- document scenario semantics for profit formulas.

## Non-goals

- unexplained fee constants scattered through the application;
- changing unrelated financial models.

## Hard rules

- Preserve deterministic financial truth.
- Do not invent fee rules; use verified project policy or VERIFY.
- Add focused unit tests for arithmetic, fees, boundaries, and rounding semantics.
- Never weaken an existing test.

## Execution

1. Inspect ticket, ADR-005, financial specification, and existing domain code.
2. Make a maximum five-step plan.
3. Implement the smallest coherent financial core.
4. Add focused deterministic tests.
5. Run narrow tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Run the relevant unit tests, including boundary/large-value cases. Confirm money remains integer
copper throughout the calculation path.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
