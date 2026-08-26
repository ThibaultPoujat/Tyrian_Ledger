# TKT-M7-03 - Implement descriptive historical analytics

## Milestone
M7

## Goal
Compute percentiles, volatility, drawdown, spread persistence, and liquidity stability from local observations.

## Dependencies
M7-01,M7-02

## Acceptance criteria
- [ ] Historical metrics are computed only from available local observations.
- [ ] Sufficient sample checks prevent misleading statistics.
- [ ] Charts/metrics disclose observation window and sample count.
- [ ] No claim of future prediction is made.

## Required tests
- [ ] Known time series metrics.
- [ ] Insufficient sample test.
- [ ] Missing observation test.

## Non-goals
- Machine-learning price forecasts.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
