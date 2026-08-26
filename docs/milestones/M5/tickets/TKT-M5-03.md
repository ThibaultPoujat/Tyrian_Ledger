# TKT-M5-03 - Implement owned-item opportunity cost

## Milestone
M5

## Goal
Value owned materials economically and compare with buying them.

## Dependencies
M3-01,M5-02

## Acceptance criteria
- [ ] Owned materials are never assumed free.
- [ ] Compute a realizable economic value using configured market evidence.
- [ ] Support owned/buy/mixed strategies where data is sufficient.
- [ ] Flag bound/unavailable/non-sellable items where relevant data exists.

## Required tests
- [ ] Owned vs bought example.
- [ ] Partial owned stock.
- [ ] Unavailable price.
- [ ] Non-economic ownership restriction.

## Non-goals
- Assuming all inventory is sellable merely because it has an item ID.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
