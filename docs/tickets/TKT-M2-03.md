# TKT-M2-03 - Implement cache with freshness metadata

## Milestone
M2

## Goal
Cache public market responses and expose data age.

## Dependencies
M2-01,M2-02

## Acceptance criteria
- [ ] Cache entries have capture time and expiry policy.
- [ ] Cache hit avoids network request.
- [ ] Cache invalidation/refresh is deterministic.
- [ ] Data freshness is available to analytics and UI.

## Required tests
- [ ] Hit/miss tests.
- [ ] Expiry test.
- [ ] Concurrent cache fill test.

## Non-goals
- Historical market database.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
