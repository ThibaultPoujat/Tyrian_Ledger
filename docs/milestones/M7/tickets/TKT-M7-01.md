# TKT-M7-01 - Design local market snapshot schema and sampling policy

## Milestone
M7

## Goal
Create the foundation for historical data without over-collecting.

## Dependencies
M2-03,M6-04

## Acceptance criteria
- [ ] Store timestamped price/order-book snapshots with source freshness.
- [ ] Define sampling policy by item class/watchlist.
- [ ] Estimate local storage growth.
- [ ] Allow future changes without corrupting existing history.

## Required tests
- [ ] Schema migration test.
- [ ] Sampling policy unit tests.

## Non-goals
- Full-frequency capture of every item.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
