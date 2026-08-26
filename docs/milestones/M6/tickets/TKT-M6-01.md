# TKT-M6-01 - Create operation history model and local persistence

## Milestone
M6

## Goal
Store saved/planned operations and their calculation context.

## Dependencies
M4-04

## Acceptance criteria
- [ ] Store operations with timestamps and calculation/configuration version identifiers.
- [ ] Preserve the scenario assumptions used at save time.
- [ ] Allow user to mark status such as planned/in-progress/completed/cancelled.

## Required tests
- [ ] Persistence round trip.
- [ ] Schema migration test.

## Non-goals
- Syncing history to a cloud service.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
