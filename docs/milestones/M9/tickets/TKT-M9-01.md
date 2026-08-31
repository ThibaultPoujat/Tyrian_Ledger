# TKT-M9-01 - Retire non-MVP product paths

## Goal

Remove the historical-market, account-key, account-data, crafting, owned-item,
operation-history, reconciliation, personal-history, and investment-research
paths that conflict with the M9 MVP. Add the approved forward migration that
deletes their stored data during upgrade.

## Dependencies

- TKT-M9-00.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- Existing database migrations, API routes, services, browser routes, and
  tests required to remove these paths safely.

## Acceptance criteria

- The active product exposes only the M9 Recommendations and Settings
  experience; retired routes, navigation, and API endpoints are removed or no
  longer reachable.
- Account-key validation/storage, account reads, crafting, owned-item logic,
  operation/personal history, reconciliation, historical snapshots,
  collection scheduling, historical analytics, and investment research are
  removed from active runtime behavior.
- A forward SQLite migration deletes all retired stored data and drops only
  retired schema objects. It preserves schema/data still needed by M9.
- No credentials, account data, historical market data, recommendation
  history, or partial scan data remain persisted by the application.
- The project still builds and tests after removal; affected tests are
  replaced with M9-appropriate coverage rather than silently deleted.
- Documentation and configuration no longer advertise retired features.

## Required tests

- Migration upgrade test from the current pre-M9 schema with representative
  retired records; assert retired data is deleted and retained M9 data is
  intact.
- API and browser routing tests proving retired paths are unavailable.
- Targeted regression tests proving active read-only public-market foundations
  remain usable.
- Full relevant .NET and browser test suites.

## Non-goals

- Public whole-market discovery or item metadata work from TKT-M9-02.
- Recommendation calculations from TKT-M9-03.
- Scan orchestration from TKT-M9-04.
- New M9 user-interface implementation from TKT-M9-05.
