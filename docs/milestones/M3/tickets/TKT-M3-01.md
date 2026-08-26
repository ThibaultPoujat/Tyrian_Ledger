# TKT-M3-01 - Implement Money and fee policy

## Milestone
M3

## Goal
Create exact copper arithmetic and centralized transaction-fee policy.

## Dependencies
M2-01

## Acceptance criteria
- [ ] Money is represented in integer copper.
- [ ] No floating-point arithmetic is used for money calculations.
- [ ] Fee policy is isolated and configurable.
- [ ] Profit formulas document scenario semantics.

## Required tests
- [ ] Representative fee/profit examples.
- [ ] Boundary/large-value tests.
- [ ] Rounding tests.

## Non-goals
- Hard-coding unexplained fee constants throughout the application.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
