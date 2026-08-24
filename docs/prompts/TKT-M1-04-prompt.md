You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-04.md

Then read the relevant local-security/HTTP configuration documents.

## Mission

Complete TKT-M1-04 only.

Make loopback-only operation and safe HTTP defaults explicit.

Acceptance-critical work:
- development server binds to loopback by default;
- safe response headers compatible with the local app are applied;
- inbound query/body values are validated at API boundaries;
- document how to intentionally change binding and why that is not recommended.

## Non-goals

- LAN/public hosting support;
- unrelated network/security redesign.

## Hard rules

- Preserve local-first security defaults.
- Do not silently enable public or LAN exposure.
- Do not invent framework behavior; verify configuration semantics from project tooling.
- Add focused tests for boundary behavior where applicable.

## Execution

1. Inspect the ticket and current server/configuration code.
2. Make a maximum five-step plan.
3. Implement the smallest secure change.
4. Run focused tests/build checks.
5. Inspect the diff and acceptance criteria.
6. Stop.

Do not repeatedly summarize or reread unchanged files. After two failed attempts at the same
operation, report the blocker and stop.

## Validation

Verify default bind behavior, response headers, input validation, and relevant tests without
requiring public/LAN exposure.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
