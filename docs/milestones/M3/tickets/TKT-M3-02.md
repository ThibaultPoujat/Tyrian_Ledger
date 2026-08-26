# TKT-M3-02 - Implement order-book quantity simulation

## Milestone
M3

## Goal
Calculate acquisition and liquidation scenarios across order-book levels.

## Dependencies
M2-01,M3-01

## Acceptance criteria
- [ ] Simulate quantity across multiple buy/sell levels.
- [ ] Calculate weighted average execution price and price impact.
- [ ] Handle insufficient depth explicitly.
- [ ] Return transparent assumptions and remaining quantity.

## Required tests
- [ ] Single-level.
- [ ] Multiple-level.
- [ ] Partial-depth.
- [ ] Empty-book.
- [ ] Large quantity.

## Non-goals
- Claiming guaranteed real-world fill behavior.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
