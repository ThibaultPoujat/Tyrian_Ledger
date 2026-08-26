# TKT-M7-02 - Implement economical historical collection scheduler

## Milestone
M7

## Goal
Collect snapshots while minimizing API requests.

## Dependencies
M7-01,M2-02

## Acceptance criteria
- [ ] High-interest/watchlist items can be sampled more frequently.
- [ ] Low-interest items use a lower sampling rate.
- [ ] Scheduler respects API request budget.
- [ ] Collection pauses cleanly on rate limiting or application shutdown.

## Required tests
- [ ] Schedule calculation tests.
- [ ] Request budget tests.
- [ ] Pause/resume tests.

## Non-goals
- Aggressive historical scraping.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
