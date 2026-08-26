# TKT-M1-03 - Add test infrastructure and fixture conventions

## Milestone
M1

## Goal
Harden the test foundation created by M1-01 so later tickets have reusable deterministic test helpers, fixture conventions, and documented commands.

## Dependencies
M1-01

## Acceptance criteria
- [ ] Verify and complete the existing unit, integration, and browser-test harnesses created by M1-01; do not recreate projects unnecessarily.
- [ ] Create fixture folders and naming conventions aligned with `docs/testing/testing-strategy.md`.
- [ ] Create deterministic clock/test-data helpers where later business logic will need them.
- [ ] Document the commands for running unit, integration, and browser tests.
- [ ] Keep normal test execution offline from the real GW2 API.

## Required tests
- [ ] All active test projects execute with zero failures.
- [ ] At least one representative executable test exists at each active layer; browser coverage may remain a minimal smoke test.
- [ ] Fixture loading/validation is covered by at least one deterministic test.

## Non-goals
- Real API calls in normal test execution.
- Replacing the test framework selected by the architecture.
- Business-feature implementation.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
