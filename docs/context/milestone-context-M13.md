# Milestone Context - M13: Local Runtime and Authenticated Gateway

## User outcome

Tyrian Ledger runs locally through ASP.NET Core and can safely determine whether
a dedicated ArenaNet key permits required read-only personal TP features.

## Invariants

Loopback by default with explicit Host validation. Production UI/API are
same-origin; development CORS allowlists exact trusted origins. State-changing
local endpoints require cross-origin request protection independent of CORS.
Browser never receives/stores the key. No plaintext secret fallback. Typed
gateway owns URLs/auth transport. Missing key must not prevent safe app
startup/public-market capabilities.

TKT-M13-01 is an R3 architecture/security boundary and its binding, Host,
origin, and state-changing-request integration tests must pass before
TKT-M13-02 begins.

## External facts

Endpoint/permission/timestamp details must be verified against current ArenaNet
contracts or recorded in VERIFY before release.

## Exit

Typed tested current/completed personal TP reads exist and the next milestone
can persist them without changing secret boundaries.
