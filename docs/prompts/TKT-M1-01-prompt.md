You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-01.md

Then read only architecture and configuration documents required by this ticket.

## Mission

Complete TKT-M1-01 only.

Create the .NET/React repository structure and baseline developer tooling.

Acceptance-critical work:
- create the source/test structure from the architecture;
- create solution/project files with nullable and warnings-as-errors enabled where feasible;
- create a minimal React frontend and backend that start locally;
- document root development commands;
- add no business logic.

## Non-goals

- GW2 API access;
- business logic;
- redesigning architecture;
- application LLM integration.

## Hard rules

- Preserve the documented stack and boundaries.
- Do not invent GW2 contracts.
- Keep credential/token values out of code and configuration.
- Minimize unrelated changes.

## Execution

1. Inspect the ticket and current repository.
2. Make a maximum five-step plan.
3. Implement the smallest coherent scaffold.
4. Run the narrow build/test/startup checks.
5. Review the diff and acceptance criteria.
6. Stop.

Do not repeatedly summarize or reread unchanged files. After two failed attempts at the same
operation, report the blocker and stop.

## Validation

Run the narrow build and startup checks relevant to the projects created. Add tests only where
behavior exists; do not invent business tests.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
