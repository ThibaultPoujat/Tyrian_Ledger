You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-02.md

Then read ADR-006 and the relevant security/configuration documents.

## Mission

Complete TKT-M1-02 only.

Create configuration abstractions that keep credential values out of source control and
out of browser-visible data.

Acceptance-critical work:
- add an abstraction for credential retrieval/storage;
- support a local development environment-variable override;
- ensure credential values never enter logs or browser responses;
- provide clear configuration errors when a credential is missing.

## Non-goals

- cloud secret management;
- frontend localStorage for credentials;
- unrelated configuration refactoring.

## Hard rules

- Never hard-code or persist credential/token values in source, browser storage, logs,
  fixtures, prompts, or tests.
- Preserve ADR-006 and the local-first security model.
- Minimize unrelated changes.

## Execution

1. Inspect the ticket, ADR-006, security rules, and current configuration.
2. Make a maximum five-step plan.
3. Implement the smallest secure abstraction.
4. Add focused tests for missing/valid configuration and redaction behavior.
5. Run narrow tests, inspect the diff, and verify no credential values are present.
6. Stop.

Do not repeatedly reread unchanged files. If the secure mechanism cannot be implemented
without contradicting ADR-006, report BLOCKED after two attempts.

## Validation

Run the relevant unit/integration tests and inspect logs/response models as applicable.
Never use a real credential in tests.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
