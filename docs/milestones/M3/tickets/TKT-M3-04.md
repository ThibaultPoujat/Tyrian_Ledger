# TKT-M3-04 - Implement deterministic opportunity scoring

## Milestone
M3

## Goal
Rank opportunities using transparent weights and penalties.

## Dependencies
M3-03

## Acceptance criteria
- [ ] Score is deterministic for identical inputs/configuration.
- [ ] Weights can be configured without changing code.
- [ ] Score includes at least profit, capital efficiency, liquidity, freshness, risk, and complexity where applicable.
- [ ] UI-facing explanation metadata is produced.

## Required tests
- [ ] Ordering tests.
- [ ] Tie tests.
- [ ] Weight sensitivity tests.
- [ ] Stale-data penalty test.

## Non-goals
- LLM-generated ranking.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
