You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M8.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M8-04.md

Then read setup, backup, release, and known-limitations documentation relevant to this ticket.

## Mission

Complete TKT-M8-04 only.

Make the project reproducible for the owner.

Acceptance-critical work:
- one documented setup path works on a clean Mac environment;
- one documented run/test command set exists;
- configuration and backup instructions are complete;
- known limitations and deferred features are listed;
- the release package contains no credential/token values or private account data.

## Non-goals

- public deployment;
- cloud backup;
- adding new product features.

## Hard rules

- Do not include credential/token values or private account data in release documentation.
- Keep setup instructions consistent with the actual repository.
- Do not claim a command works without running or otherwise verifying it.
- Add only release/documentation fixes required by the ticket.

## Execution

1. Inspect ticket, README, configuration, setup, and backup documents.
2. Make a maximum five-step plan.
3. Validate the documented setup/run/test path and fix discrepancies in scope.
4. Review release contents and known limitations.
5. Validate acceptance criteria and diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use a clean or isolated environment where practical. Record exactly which setup/run/test commands
were verified and which could not be verified.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
