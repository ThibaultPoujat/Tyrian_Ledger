# TKT-M1-04 - Add local-only runtime and baseline security middleware

## Milestone
M1

## Goal
Make loopback-only operation and safe HTTP defaults explicit.

## Dependencies
M1-01,M1-02

## Acceptance criteria
- [ ] Default development server binds to loopback.
- [ ] Add safe response headers compatible with the local app.
- [ ] Validate inbound query/body values at API boundaries.
- [ ] Document how to intentionally change the binding and why doing so is not recommended.

## Required tests
- [ ] Configuration test asserts loopback default.
- [ ] Security header smoke test.

## Non-goals
- LAN/public hosting support.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
