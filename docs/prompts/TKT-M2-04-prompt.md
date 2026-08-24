You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M2.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M2-04.md

Then read the gateway, rate-limit, security, and diagnostics requirements explicitly relevant to this ticket.

## Mission

Complete TKT-M2-04 only.

Make API use and cache behavior auditable through safe local diagnostics.

Acceptance-critical work:
- track request counts, cache hits/misses, latency, 429s, and parsing failures;
- never expose credential/token values or authorization headers;
- provide a safe local diagnostic view or structured diagnostic endpoint.

## Non-goals

- remote telemetry;
- analytics unrelated to API/cache diagnostics.

## Hard rules

- Diagnostics must be safe to display locally.
- Never log credential/token values, authorization headers, or sensitive account payloads.
- Preserve the read-only gateway.
- Add tests for redaction and diagnostic aggregation.

## Execution

1. Inspect ticket, diagnostics requirements, gateway, and current logging.
2. Make a maximum five-step plan.
3. Implement the smallest diagnostic surface.
4. Add focused tests for metrics and redaction.
5. Run narrow tests, inspect diff, and stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the exact blocker.

## Validation

Inspect logs/diagnostic responses with synthetic data and confirm sensitive values are excluded.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
