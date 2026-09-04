# Milestone Context - M13: Local Runtime and Authenticated Gateway

## User outcome

Tyrian Ledger runs locally through ASP.NET Core and can safely determine whether
a dedicated ArenaNet key permits required read-only personal TP features.

## Invariants

Loopback by default. Browser never receives/stores the key. No plaintext secret
fallback. Typed gateway owns URLs/auth transport. Missing key must not prevent
safe app startup/public-market capabilities.

## External facts

Endpoint/permission/timestamp details must be verified against current ArenaNet
contracts or recorded in VERIFY before release.

## Exit

Typed tested current/completed personal TP reads exist and the next milestone
can persist them without changing secret boundaries.
