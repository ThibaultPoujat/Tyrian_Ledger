# TKT-M9-02 - Public whole-market discovery foundations

## Goal

Add typed, verified public data access for whole-market candidate discovery and
finalist item metadata, while retaining no market history.

## Dependencies

- TKT-M9-01.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- [GW2 endpoint matrix](../../../architecture/gw2-endpoint-matrix.md)
- Existing typed gateway, scheduler, DTO, cache, and public commerce tests.

## Acceptance criteria

- The typed gateway can obtain the public item-ID index, current aggregate
  commerce prices, current detailed listings for a bounded finalist set, and
  item metadata needed to show name and normal stack limit.
- The implementation confirms the exact public endpoint schemas, batching,
  paging/206 behavior, and version pins used by M9 before relying on them.
  VERIFY-004, VERIFY-005, VERIFY-006, and VERIFY-007 are updated with
  evidence or remain explicitly open with safe limits.
- Feature/application code does not construct ArenaNet URLs; external DTOs
  remain separate from domain values.
- Batching and concurrency are bounded by the existing gateway/scheduler
  policy. Missing, malformed, or incomplete final-candidate data is reported
  as an incomplete scan input rather than guessed.
- Current market data is held only for the scan/request lifetime. No snapshots,
  listings, prices, item metadata, or rankings are persisted for history.
- Gateway behavior remains public and keyless.

## Required tests

- Typed deserialization and schema-contract tests using sanitized public
  fixtures for each new endpoint/response shape.
- Batch boundary, paging/206, cancellation, rate-limit, and partial-data
  failure tests.
- Tests proving no feature code bypasses the typed gateway and no historical
  persistence is introduced.
- A safe, documented live verification only if needed to resolve a required
  contract; record evidence in VERIFY.

## Non-goals

- Financial eligibility, risk profiles, quantity policy, or ranking from
  TKT-M9-03.
- Player-visible scan state and result publication from TKT-M9-04.
- Browser pages, onboarding, or recommendation cards from TKT-M9-05.
