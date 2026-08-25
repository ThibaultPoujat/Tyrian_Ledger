You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-04.md

Then read only the local-security/HTTP configuration documents explicitly needed by the ticket.

## Mission

Complete TKT-M1-04 only.

Make loopback-only operation, safe HTTP defaults, and a reusable input-validation boundary explicit
for the current local skeleton.

## Acceptance-critical work

- development/test startup defaults to loopback;
- apply a minimal, documented set of response-security headers appropriate to the current local app;
- establish reusable API-boundary input validation without inventing future business endpoints;
- document intentional bind overrides and their risks.

Do not enable HSTS unless HTTPS is actually configured.
Do not create a fake business endpoint merely to demonstrate validation.

## Hard rules

- Preserve local-first security defaults.
- Do not silently enable public or LAN exposure.
- Do not invent framework behavior; verify configuration semantics from the current project/tooling.
- Do not add authentication/authorization yet.
- Do not add business API features.
- Add focused tests for every behavior introduced.

## Execution

1. Inspect the current server/configuration code and the ticket.
2. Make a maximum five-step plan.
3. Implement the smallest secure change.
4. Add focused tests for bind defaults, headers, and the validation mechanism.
5. Run focused tests/build checks and inspect the diff.
6. Stop.

Do not repeatedly summarize or reread unchanged files.
After two failed attempts at the same operation, report the blocker and stop.

## Validation

Verify:
- the default bind is loopback;
- selected headers are present and compatible with the local app;
- no HSTS is emitted without HTTPS;
- the reusable validation mechanism is deterministic and covered by tests;
- no LAN/public exposure was introduced.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
