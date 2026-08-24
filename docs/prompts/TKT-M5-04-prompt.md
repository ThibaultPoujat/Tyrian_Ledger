You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M5.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M5-04.md

Then read crafting, recipe, account-discipline, and opportunity-cost specifications relevant to this ticket.

## Mission

Complete TKT-M5-04 only.

Analyze profitable crafting paths with feasibility constraints and controlled recursion.

Acceptance-critical work:
- represent recipe output/ingredient relationships;
- apply verified discipline/rating and recipe availability constraints;
- detect cycles and enforce configurable depth/candidate limits;
- memoize repeated subproblems;
- report truncation and unknowns explicitly;
- compare purchase cost with owned-material opportunity cost.

## Non-goals

- exhaustive unlimited world optimization;
- unbounded recursive search;
- assuming missing recipe/account data is complete.

## Hard rules

- Preserve deterministic calculations and integer copper.
- Never invent recipe/account fields.
- Search limits must be explicit and testable.
- Add unit tests for cycles, depth limits, memoization, feasibility, and opportunity-cost choices.

## Execution

1. Inspect ticket, recipe/account models, and existing financial components.
2. Make a maximum five-step plan.
3. Implement the smallest bounded graph/search service.
4. Add focused tests, including pathological graphs.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic recipe graphs and account data. Confirm search terminates under configured limits
and reports truncation/unknown states rather than silently returning a false optimum.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
