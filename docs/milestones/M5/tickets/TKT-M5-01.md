# TKT-M5-01 - Implement token validation and permission-aware account access

## Milestone
M5

## Goal
Validate an API key and expose only permitted features.

## Dependencies
M0-02,M0-04,M1-02,M2-02

## Acceptance criteria
- [ ] Use tokeninfo or the verified equivalent to validate the key.
- [ ] Display safe permission status.
- [ ] Never return the key to the browser.
- [ ] Disable unsupported account features when scopes are missing.
- [ ] Render token metadata such as key name as text, not HTML.

## Required tests
- [ ] Valid token fixture.
- [ ] Missing permission fixture.
- [ ] Malformed metadata/XSS string fixture.
- [ ] Secret non-disclosure test.

## Non-goals
- Storing the API key in frontend localStorage.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
