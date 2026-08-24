You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M6.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M6-01.md

Then read local persistence/history specifications relevant to this ticket.

## Mission

Complete TKT-M6-01 only.

Store saved/planned operations with their calculation context.

Acceptance-critical work:
- store timestamps and calculation/configuration version identifiers;
- preserve scenario assumptions at save time;
- support planned/in-progress/completed/cancelled states.

## Non-goals

- cloud synchronization;
- remote accounts;
- rewriting unrelated persistence.

## Hard rules

- Keep local-first storage.
- Do not store credential/token values in operation records.
- Preserve deterministic calculation semantics.
- Add tests for persistence, status transitions, and snapshot/version integrity.

## Execution

1. Inspect ticket, persistence schema, and existing domain models.
2. Make a maximum five-step plan.
3. Implement the smallest coherent history model.
4. Add focused tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Verify stored records can reproduce their saved scenario context and incomplete operations are
not silently treated as realized results.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
