You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M5.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M5-01.md

Then read the endpoint matrix, security/credential rules, and account-feature specification relevant to this ticket.

## Mission

Complete TKT-M5-01 only.

Validate a GW2 API key and expose only features allowed by verified permissions.

Acceptance-critical work:
- use tokeninfo or the verified equivalent;
- expose safe permission status;
- never return the credential/token to the browser;
- disable unsupported account features when permissions are missing;
- render safe token metadata as text.

## Non-goals

- frontend localStorage for credentials;
- generic authenticated API methods;
- write-capable GW2 operations.

## Hard rules

- Follow ADR-006 and the endpoint/permission matrix.
- Never log, persist, or return credential/token values.
- Never invent permission names or tokeninfo fields; use VERIFY when uncertain.
- Add tests for permission gating, redaction, and error handling.

## Execution

1. Inspect ticket, credential architecture, and verified endpoint matrix.
2. Make a maximum five-step plan.
3. Implement the smallest typed authenticated read-only path.
4. Add focused tests with synthetic responses.
5. Run narrow tests and inspect the diff for credential leakage.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic tokeninfo/account responses. Confirm credential/token values cannot reach browser
responses or logs and unsupported features are disabled safely.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
