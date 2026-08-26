# TKT-M5-02 - Implement account snapshot ingestion

## Milestone
M5

## Goal
Add bank/materials and relevant character data with caching and local scoping.

## Dependencies
M5-01,M2-03

## Acceptance criteria
- [ ] Fetch only endpoints required for enabled account features.
- [ ] Cache account snapshots separately from public market data.
- [ ] Associate snapshots with a local account profile identifier.
- [ ] Handle missing permissions and partial data gracefully.

## Required tests
- [ ] Fixture ingestion tests.
- [ ] Permission failure test.
- [ ] Cache isolation test.

## Non-goals
- Persisting unnecessary full raw payloads indefinitely.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
