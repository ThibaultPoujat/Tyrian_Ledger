# TKT-M3-03 - Implement flip profitability and liquidity analysis

## Milestone
M3

## Goal
Generate deterministic flip scenarios and a liquidity proxy.

## Dependencies
M3-01,M3-02

## Acceptance criteria
- [ ] Compute modeled net profit, ROI, capital required, price impact, and liquidity metrics.
- [ ] Support configurable minimum profit and capital constraints.
- [ ] Mark missing/stale data as unusable or lower confidence according to policy.
- [ ] Each result explains the scenario used.

## Required tests
- [ ] Known-good fixture.
- [ ] Known-bad negative-profit fixture.
- [ ] Stale-data fixture.
- [ ] Insufficient-depth fixture.

## Non-goals
- Automated orders.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
