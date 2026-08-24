You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-03.md

Then read `docs/testing/testing-strategy.md` and fixture guidance relevant to this ticket.

## Mission

Complete TKT-M1-03 only.

Create reusable test helpers and a deterministic fixture policy.

Acceptance-critical work:
- create unit, integration, and browser test projects or equivalent harnesses;
- create fixture folders and naming conventions;
- create deterministic clock/test-data helpers where needed;
- document test commands;
- keep normal tests offline from the real GW2 API.

## Non-goals

- real API calls during normal test execution;
- application feature implementation;
- unrelated test-framework changes.

## Hard rules

- Tests must be deterministic and reproducible.
- No real credential/token values or account data in fixtures.
- Never weaken a test to make it pass.
- Keep fixture conventions compatible with the architecture and testing strategy.

## Execution

1. Inspect the ticket, testing strategy, and current test structure.
2. Make a maximum five-step plan.
3. Implement the smallest reusable harness/helpers.
4. Run the narrow test suite and verify fixture safety.
5. Inspect the diff and acceptance criteria.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts at the same operation,
report the exact blocker and stop.

## Validation

Run the new/affected test projects and confirm deterministic helpers and fixture loading work.
Do not call the live GW2 API.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
