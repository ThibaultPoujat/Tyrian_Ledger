You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M0.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M0-SETUP-01.md

Then read only the workflow/context files explicitly required by this ticket.

## Mission

Complete TKT-M0-SETUP-01 only.

This is a documentation/workflow maintenance ticket. Establish the VERIFY register and
integrate its workflow into the lightweight Qwen development process.

Acceptance-critical work:
- maintain stable VERIFY IDs, statuses, owner tickets, and dates;
- preserve the three unresolved M0-01 verification items;
- require pre-ticket register review and maintenance;
- keep the register as the project-level index while evidence remains in tickets;
- document before/during/completion VERIFY phases;
- update the prompt-generation workflow if required.

## Non-goals

- application code;
- architecture/specification changes;
- ADR creation solely for the register;
- application-side VERIFY APIs.

## Hard rules

- Never invent external facts.
- Do not resolve a VERIFY item without evidence.
- Do not delete resolved history.
- Keep changes limited to workflow/context documentation.

## Execution

1. Inspect the ticket, current register, and relevant workflow/context files.
2. Make a maximum five-step plan.
3. Apply the smallest coherent documentation change.
4. Check register IDs/statuses and cross-document consistency.
5. Validate acceptance criteria and diff.
6. Stop.

Do not repeatedly reread unchanged documents. Do not reopen completed investigations without
new evidence. If a material uncertainty is discovered, record it in the register.

## Validation

Check Markdown consistency, stable VERIFY IDs, required workflow references, and absence of
credential/token values. Do not create application tests for this documentation ticket.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
