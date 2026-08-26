# TKT-M8-01 - Perform security review and secret-leak audit

## Milestone
M8

## Goal
Verify no API key or sensitive account data leaks through code, logs, browser, fixtures, or Git.

## Dependencies
M6-04,M5-01

## Acceptance criteria
- [ ] Run secret scanning on repository.
- [ ] Inspect network/browser responses for API-key disclosure.
- [ ] Inspect logs and exception pages for secrets.
- [ ] Confirm local bind/security defaults.
- [ ] Document remaining risks.

## Required tests
- [ ] Automated secret-pattern test where appropriate.
- [ ] Manual security checklist sign-off.

## Non-goals
- Guaranteeing zero vulnerabilities.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
