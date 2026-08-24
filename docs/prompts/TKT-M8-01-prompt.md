You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M8.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M8-01.md

Then read the security checklist and local-network security requirements relevant to this ticket.

## Mission

Complete TKT-M8-01 only.

Verify that credential/token values and sensitive account data do not leak through code, logs,
browser responses, fixtures, or Git.

Acceptance-critical work:
- run repository secret scanning;
- inspect browser/network responses for credential disclosure;
- inspect logs and exception pages;
- confirm local bind/security defaults;
- document remaining risks.

## Non-goals

- guaranteeing zero vulnerabilities;
- public penetration testing.

## Hard rules

- Do not expose or reproduce any discovered credential/token value.
- If a real credential is discovered, stop, redact it from the report, and treat it as a security
  incident requiring human handling.
- Preserve local-only defaults.
- Do not weaken security checks to obtain a clean result.

## Execution

1. Inspect ticket and security checklist.
2. Make a maximum five-step plan.
3. Perform the requested scans/checks.
4. Fix only findings in ticket scope or record them clearly as follow-up.
5. Validate and inspect the diff.
6. Stop.

Do not repeatedly scan the same unchanged state. After two failed attempts, report the blocker.

## Validation

Use safe synthetic values where test data is needed. Never paste a real credential/token into
logs, prompts, issues, tests, or the final report.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete. Never include credential/token
values.
