# TKT-M2-01 - Implement typed GW2 API client for public market data

## Milestone
M2

## Goal
Implement typed read-only requests for prices and listings based on verified schemas.

## Dependencies
M0-02,M0-03,M1-03

## Acceptance criteria
- [ ] Only explicit GET methods are exposed.
- [ ] External DTOs remain separate from domain models.
- [ ] Batch IDs are supported where the verified endpoint allows them.
- [ ] Unexpected fields do not crash parsing unnecessarily.
- [ ] Errors map to stable error categories.

## Required tests
- [ ] Fixture tests for 200 responses.
- [ ] Malformed JSON test.
- [ ] HTTP error mapping tests.

## Non-goals
- Account/authenticated endpoints other than those explicitly needed by later tickets.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
