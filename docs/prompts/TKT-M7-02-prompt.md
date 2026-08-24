You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M7.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M7-02.md

Then read historical sampling policy and rate-limit/gateway requirements relevant to this ticket.

## Mission

Complete TKT-M7-02 only.

Collect historical snapshots while minimizing API requests.

Acceptance-critical work:
- sample high-interest/watchlist items more frequently;
- use lower sampling rates for low-interest items;
- respect the API request budget;
- pause cleanly on rate limiting or application shutdown.

## Non-goals

- aggressive historical scraping;
- collecting every item at high frequency;
- bypassing the centralized scheduler.

## Hard rules

- All collection goes through the single gateway and rate manager.
- Do not invent quotas; use configured policy.
- Scheduler behavior must be deterministic and stoppable.
- Add tests for scheduling, prioritization, pause/resume, shutdown, and rate-limit handling.

## Execution

1. Inspect ticket, historical policy, gateway, and scheduler code.
2. Make a maximum five-step plan.
3. Implement the smallest bounded collector.
4. Add focused deterministic tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Use mocks/fake clocks. Do not stress the live GW2 API. Confirm the scheduler honors configured
request policy and shuts down without leaving uncontrolled work running.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
