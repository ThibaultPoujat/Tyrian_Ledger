# TKT-M0-03 - Validate API quotas, caching guidance, and error behavior

## Milestone
M0

## Goal
Turn rate-limit and external-contract assumptions into measurable configuration requirements.

## Dependencies
None

## Acceptance criteria
- [ ] Document current rate-limit guidance from authoritative/community documentation.
- [ ] Define application-level configurable scheduler parameters without hard-coding an unverified quota.
- [ ] Document 429 handling and retry behavior.
- [ ] Define which live verification is safe and how it will be performed without deliberately stressing the API.

## Required tests
- [ ] A rate-limit policy document exists.
- [ ] A mock 429 scenario is specified.

## Non-goals
- Load testing the real GW2 API.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
