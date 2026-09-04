# Milestone Context - M14: Durable Personal Data

## User outcome

Personal completed TP history survives application restarts and remote history
window expiry, current orders synchronize safely, and the user can back up or
clear local data intentionally.

## Invariants

SQLite never stores the API key. Completed external IDs are unique in account
scope. Repeated sync is idempotent. Partial remote failure cannot erase
last-known-good state. Destructive migration/clear requires explicit safeguards.

## Exit

The local database is trustworthy enough to become the accounting source in
M15 and has documented recovery controls.
