You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M2.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M2-02.md

Then read the rate-limit policy, endpoint matrix, and gateway architecture required by this ticket.

## Mission

Complete TKT-M2-02 only.

Centralize GW2 request rate management.

Acceptance-critical work:
- route requests through one scheduler/rate-management component;
- deduplicate concurrent identical requests;
- apply bounded backoff to 429 responses;
- distinguish transient and permanent failures;
- keep quota configuration explicit rather than hard-coding an unverified assumption.

## Non-goals

- live stress testing;
- aggressive request generation;
- unrelated API client redesign.

## Hard rules

- Never invent quotas or retry semantics.
- Preserve the single gateway and read-only boundary.
- Use configuration for uncertain policy values.
- Add deterministic tests for scheduling, deduplication, 429 handling, and failure classification.

## Execution

1. Inspect ticket, policy, gateway, and current request code.
2. Make a maximum five-step plan.
3. Implement the smallest centralized mechanism.
4. Add focused deterministic tests with controlled time.
5. Run narrow tests, inspect diff, and stop.

Do not repeatedly reread unchanged files. Do not retry the same failed operation more than twice.
If blocked after two attempts, report the exact blocker.

## Validation

Use mocks/fixtures and deterministic clocks. Do not stress the live GW2 API.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
