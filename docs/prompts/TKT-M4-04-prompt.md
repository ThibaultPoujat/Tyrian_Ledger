You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M4.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M4-04.md

Then read session-planning/UX requirements relevant to this ticket.

## Mission

Complete TKT-M4-04 only.

Turn user constraints into a practical shortlist without pretending to know execution time.

Acceptance-critical work:
- support effort categories: very low, low, medium, high, ongoing/patient;
- respect capital, risk, and strategy preferences;
- produce an ordered session shortlist;
- state clearly that effort categories are approximations, not time guarantees.

## Non-goals

- precise execution-time prediction;
- user-specific time models before adequate data exists.

## Hard rules

- Keep planner behavior deterministic.
- Do not infer exact execution duration from market data.
- Make ranking/constraints explainable.
- Add focused tests for planner ordering and constraint behavior.

## Execution

1. Inspect ticket, preference models, opportunity scoring, and UX rules.
2. Make a maximum five-step plan.
3. Implement the smallest planner change.
4. Add focused tests.
5. Run narrow tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Confirm deterministic ordering and correct application of capital/risk/preferences. Confirm UI
wording does not imply precise execution time.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
