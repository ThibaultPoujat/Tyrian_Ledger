# TKT-M2-02 - Build request scheduler, deduplication, and retry policy

## Milestone
M2

## Goal
Centralize GW2 request rate management.

## Dependencies
M2-01

## Acceptance criteria
- [ ] Requests are scheduled through one component.
- [ ] Concurrent identical requests are deduplicated.
- [ ] 429 responses trigger bounded backoff.
- [ ] Retry rules distinguish transient from permanent failures.
- [ ] Configured quotas are not hard-coded from an unverified assumption.

## Required tests
- [ ] Concurrency deduplication test.
- [ ] 429 backoff test.
- [ ] No retry for permanent auth/permission failure.

## Non-goals
- Stress-testing live GW2.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
