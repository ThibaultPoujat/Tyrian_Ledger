# TKT-M2-04 - Add market-data observability and diagnostics

## Milestone
M2

## Goal
Make API use and cache behavior auditable.

## Dependencies
M2-02,M2-03

## Acceptance criteria
- [ ] Track request counts, cache hits/misses, latency, 429s, and parsing failures.
- [ ] Never log API keys or authorization headers.
- [ ] Provide a local diagnostic view or structured diagnostic endpoint that is safe to display.

## Required tests
- [ ] Sensitive-value redaction test.
- [ ] Counter increments on mock requests.

## Non-goals
- Remote telemetry.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
