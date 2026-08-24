You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M4.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M4-01.md

Then read UX and opportunity-model documents explicitly relevant to this ticket.

## Mission

Complete TKT-M4-01 only.

Build the main desktop dashboard around deterministic opportunities.

Acceptance-critical work:
- show ranked opportunities with capital, modeled profit, ROI, liquidity, risk/confidence, and data age;
- provide filters for capital, minimum profit, strategy, risk, and freshness;
- avoid unsupported guarantee language;
- provide explicit loading, empty, and error states.

## Non-goals

- mobile-first UI;
- executing Trading Post actions;
- inventing opportunity calculations in the UI.

## Hard rules

- Keep calculations in backend/application services; UI presents their results.
- Preserve read-only behavior.
- Use accessible, deterministic UI states.
- Add browser/unit tests for critical interaction behavior where appropriate.

## Execution

1. Inspect ticket, UX guidance, and existing API/view models.
2. Make a maximum five-step plan.
3. Implement the smallest dashboard surface.
4. Add focused tests.
5. Run narrow tests/browser checks and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify filters, loading/error/empty states, key metrics, and read-only UI behavior with deterministic
test data. Do not introduce unsupported claims of profit certainty.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
