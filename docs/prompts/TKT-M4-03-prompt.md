You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M4.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M4-03.md

Then read local persistence and preference specifications relevant to this ticket.

## Mission

Complete TKT-M4-03 only.

Persist user capital and opportunity preferences locally.

Acceptance-critical work:
- store capital, minimum profit, risk preference, strategy preference, and allocation constraints locally;
- validate numeric ranges and sensible defaults;
- changing preferences deterministically re-ranks or filters results.

## Non-goals

- remote user accounts;
- cloud synchronization;
- unrelated persistence redesign.

## Hard rules

- Preserve local-first storage.
- Do not store credential/token values with user preferences.
- Validate all user input at the application boundary.
- Add deterministic tests for validation, persistence, and preference effects.

## Execution

1. Inspect ticket, persistence architecture, and existing preference models.
2. Make a maximum five-step plan.
3. Implement the smallest coherent local persistence change.
4. Add focused tests.
5. Run narrow tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify sensible defaults, invalid-input handling, deterministic filtering/ranking, and local-only
persistence.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
