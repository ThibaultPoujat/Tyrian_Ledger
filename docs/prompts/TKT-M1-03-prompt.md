You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-03.md

Then read:
- docs/testing/testing-strategy.md
- only the fixture guidance explicitly relevant to the files you need to change

## Mission

Complete TKT-M1-03 only.

Harden the test foundation already created by M1-01. Reuse the existing projects and harnesses;
do not recreate them unless inspection proves they are incomplete or incorrect.

## Acceptance-critical work

- verify/complete unit, integration, and browser-test harnesses;
- establish deterministic fixture folders/naming;
- add reusable clock/test-data helpers only where justified;
- document test commands;
- keep normal tests offline from the live GW2 API.

## Hard rules

- Tests must be deterministic and reproducible.
- No real credentials, account data, or live API calls in normal tests.
- Never weaken or delete a test to make it pass.
- Do not replace the selected xUnit/Playwright stack.
- Do not implement application features.
- Do not create duplicate test projects when an existing project can be completed.

## Execution

1. Inspect the ticket, testing strategy, and current test tree.
2. Make a maximum five-step plan.
3. Complete the smallest missing infrastructure/helper work.
4. Add representative executable tests and deterministic fixture coverage.
5. Run the affected test projects, then broader backend validation.
6. Stop.

Do not repeatedly reread unchanged files.
After two failed attempts at the same operation, report the exact blocker and stop.

## Validation

Verify:
- all active test projects run without failures;
- fixture loading is deterministic;
- representative unit/integration/browser tests execute as appropriate;
- normal tests do not call the live GW2 API.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
