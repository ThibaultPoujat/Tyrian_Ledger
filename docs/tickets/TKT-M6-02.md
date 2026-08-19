# TKT-M6-02 - Implement realized profit reconciliation

## Milestone
M6

## Goal
Calculate realized profit separately from unrealized P/L.

## Dependencies
M6-01,M3-01

## Acceptance criteria
- [ ] Realized profit uses recorded actual acquisition/sale values and fees.
- [ ] Unrealized value is separately labeled.
- [ ] Incomplete operations are not silently counted as realized profit.
- [ ] Profit statistics are reproducible from stored records.

## Required tests
- [ ] Complete trade.
- [ ] Partial trade.
- [ ] Cancelled trade.
- [ ] Fee variation.
- [ ] Unrealized-only record.

## Non-goals
- Claiming lifetime profit before local history starts.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
