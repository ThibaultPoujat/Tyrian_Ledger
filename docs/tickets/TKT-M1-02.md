# TKT-M1-02 - Add local configuration and secret-store abstraction

## Milestone
M1

## Goal
Create configuration interfaces that never require putting the API key in source control.

## Dependencies
M1-01

## Acceptance criteria
- [ ] Add an abstraction for secret retrieval/storage.
- [ ] Support a local development environment variable override.
- [ ] Ensure API key values are excluded from logs and browser responses.
- [ ] Add clear configuration error messages when a secret is missing.

## Required tests
- [ ] Missing-secret test.
- [ ] Log redaction test.
- [ ] Browser endpoint does not expose secret test.

## Non-goals
- Adding cloud secret management.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
