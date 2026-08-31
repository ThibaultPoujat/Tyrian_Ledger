# TKT-M9-04 - Player-triggered scan lifecycle

## Goal

Implement the complete, bounded scan lifecycle that obtains current public
market inputs, invokes deterministic recommendation logic, and publishes only
complete in-memory results.

## Dependencies

- TKT-M9-03.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- Typed public gateway and recommendation contracts from TKT-M9-02 and
  TKT-M9-03.

## Acceptance criteria

- A player explicitly starts a scan. No background schedule, automatic polling,
  stale auto-refresh, or historical collection is added.
- The lifecycle follows the approved sequence: whole public item and aggregate
  price discovery, cheap deterministic screening, bounded finalist detailed
  listings/metadata reads, recommendation computation, then publication.
- The scan has explicit idle, running/progress, complete, cancelled,
  rate-limited, and failed states. Progress is meaningful but does not expose
  unsupported accuracy or time promises.
- Cancellation stops pending work through the gateway/scheduler boundary and
  publishes no results.
- Rate-limit, transport, malformed-response, and incomplete-finalist failures
  publish no partial recommendations, preserve no partial results, and provide
  a retryable user-safe outcome.
- Recommendation inputs/results stay in memory for the active request/session;
  no market, recommendation, or scan history persistence is introduced.
- The scan response carries the completed scan time, result groups, and enough
  structured explanatory data for the UI. It does not expose secrets or raw
  external DTOs.

## Required tests

- Lifecycle state-machine tests for successful scan, cancellation, rate limit,
  transient/permanent failure, malformed data, and incomplete finalist data.
- Tests for bounded finalist count, batching, no duplicate concurrent scan for
  one player/session, and retry after failure.
- API integration tests proving completed results are returned only after all
  required inputs succeed and partial results are absent.
- Tests proving no scheduler, database, or cache writes retain market or scan
  history.

## Non-goals

- Browser UX, settings forms, visual progress design, or accessibility work
  from TKT-M9-05.
- Changes to deterministic financial policy from TKT-M9-03.
- Historical refresh, notifications, or post-trade status tracking.
