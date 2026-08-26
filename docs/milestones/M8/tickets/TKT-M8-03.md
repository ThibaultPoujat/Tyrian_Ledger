# TKT-M8-03 - Performance and API-request efficiency review

## Milestone
M8

## Goal
Ensure the MVP remains responsive and API-efficient.

## Dependencies
M3-04,M5-04,M7-02

## Acceptance criteria
- [ ] Benchmark representative analytical workloads with local fixtures.
- [ ] Confirm candidate screening prevents unnecessary deep listings requests.
- [ ] Confirm cache and request deduplication behavior.
- [ ] Document any known computational limits for crafting search.

## Required tests
- [ ] Performance smoke benchmark.
- [ ] Request-count regression test.

## Non-goals
- Optimizing prematurely at the cost of correctness.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
