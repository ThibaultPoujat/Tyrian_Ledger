You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M2.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M2-01.md

Then read the endpoint matrix, verified schemas, and architecture documents required by this ticket.

## Mission

Complete TKT-M2-01 only.

Implement typed read-only requests for public prices and listings based on verified schemas.

Acceptance-critical work:
- expose only explicit GET operations;
- keep external DTOs separate from domain models;
- support verified batch IDs where allowed;
- tolerate harmless unexpected response fields without hiding malformed required data;
- map failures to stable application error categories.

## Non-goals

- authenticated/account endpoints not required by this ticket;
- generic HTTP methods;
- write-capable operations.

## Hard rules

- Use the single GW2 gateway.
- Never invent endpoint fields or batching behavior.
- Do not add a generic authenticated request method.
- Use VERIFY if the current schema is uncertain.
- Add tests for parsing, mapping, and error behavior.

## Execution

1. Inspect ticket, matrix, architecture, and current gateway structure.
2. Make a maximum five-step plan.
3. Implement the smallest typed read-only surface.
4. Add focused unit/integration tests using synthetic fixtures.
5. Run narrow tests, inspect diff, and check the read-only boundary.
6. Stop.

Do not repeatedly reread unchanged documents. After two failed attempts at the same operation,
report the exact blocker and stop.

## Validation

Run relevant tests against fixtures/mocks. Do not require live API access for normal tests.
Confirm no write-capable method or generic authenticated path was introduced.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
