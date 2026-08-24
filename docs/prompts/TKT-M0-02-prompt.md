You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M0.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M0-02.md

Then read only `docs/architecture/gw2-endpoint-matrix.md` and authoritative endpoint sources
required to resolve this ticket.

## Mission

Complete TKT-M0-02 only.

Create and maintain the authoritative endpoint table used by the application.

Acceptance-critical work:
- list every MVP endpoint, purpose, required permission, batching capability, freshness,
  and cache policy;
- mark uncertain facts VERIFY;
- include only endpoints actually required by the specification;
- do not introduce undocumented endpoint dependencies;
- maintain the synthetic fixture policy required by the ticket.

## Non-goals

- implementing the HTTP client;
- changing application architecture;
- inventing undocumented batching or permission behavior.

## Hard rules

- Never invent GW2 API fields, permissions, quotas, endpoints, or behavior.
- Prefer authoritative documentation; if unavailable, use VERIFY.
- Keep endpoint DTO/domain separation and single-gateway architecture intact.
- Minimize unrelated changes.

## Execution

1. Inspect the ticket, matrix, VERIFY register, and relevant authoritative sources.
2. Make a maximum five-step plan.
3. Update the endpoint matrix/fixtures/register only as required.
4. Cross-check each claim and register unresolved material facts.
5. Validate the acceptance criteria and diff.
6. Stop.

Do not repeatedly reread unchanged sources. Do not retry the same failed fetch more than
twice. If a required fact cannot be established, record VERIFY and continue where possible.

## Validation

Validate the matrix against the cited sources. Validate fixture JSON and ensure fixtures
contain no real account data or credential/token values. No live API call is required unless
the ticket explicitly demands one.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
