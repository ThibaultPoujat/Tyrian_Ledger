You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M3.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M3-03.md

Then read the opportunity/profit specification and market-depth models relevant to this ticket.

## Mission

Complete TKT-M3-03 only.

Generate deterministic flip scenarios and a liquidity proxy.

Acceptance-critical work:
- compute modeled net profit, ROI, capital required, price impact, and liquidity metrics;
- support configurable minimum profit and capital constraints;
- treat missing/stale data according to the documented policy;
- provide scenario/explanation metadata.

## Non-goals

- automated orders;
- guaranteed fill or profit claims;
- hidden fallback assumptions.

## Hard rules

- Use deterministic integer-copper calculations.
- Make scenario assumptions explicit.
- Do not invent liquidity definitions; use the specification or VERIFY.
- Add focused unit tests for ranking inputs, constraints, stale data, and edge cases.

## Execution

1. Inspect ticket and existing financial/depth components.
2. Make a maximum five-step plan.
3. Implement the smallest deterministic opportunity service.
4. Add focused unit tests.
5. Run narrow tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use synthetic market data and verify stable results for identical inputs. Confirm unusable/stale
data cannot silently become a high-confidence opportunity.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
