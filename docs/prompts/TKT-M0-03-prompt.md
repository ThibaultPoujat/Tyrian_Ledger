You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M0.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M0-03.md

Then read only `docs/rate-limiting/rate-limit-policy.md` and the authoritative sources
needed by this ticket.

## Mission

Complete TKT-M0-03 only.

Turn rate-limit and external-contract assumptions into measurable application policy.

Acceptance-critical work:
- document current authoritative/community guidance and its verification status;
- define configurable scheduler parameters without pretending an unverified quota is fact;
- document bounded 429 handling and retry behavior;
- define safe live verification that does not deliberately stress the API.

## Non-goals

- load/stress testing the live GW2 API;
- inventing a hard-coded quota from an unverified source;
- implementing the full HTTP client unless already required by the ticket.

## Hard rules

- Never invent GW2 API fields, quotas, endpoints, or behavior.
- Use VERIFY for unresolved values.
- Do not perform deliberate load testing.
- Keep policy/configuration separate from later runtime implementation.
- Minimize unrelated changes.

## Execution

1. Inspect the ticket, rate-limit policy, VERIFY register, and relevant sources.
2. Make a maximum five-step plan.
3. Update the policy and VERIFY register as required.
4. Check that assumptions are clearly distinguished from verified facts.
5. Validate the acceptance criteria and diff.
6. Stop.

Do not repeatedly investigate the same rate-limit fact. After two failed attempts to obtain
evidence, register VERIFY and continue with safe work.

## Validation

Check the document for internally consistent retry/backoff terminology, explicit verification
status, and safe live-verification instructions. Do not stress the real API.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
