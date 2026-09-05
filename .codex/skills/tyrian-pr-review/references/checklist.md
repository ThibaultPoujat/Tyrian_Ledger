# Tyrian PR Review Checklist

Apply only relevant sections; do not generate noise from inapplicable checks.

## Universal

- Ticket outcome is actually delivered, not only scaffolding.
- Diff stays within ticket/non-goals.
- No stale source-of-truth contradiction introduced.
- Error/empty/unknown states remain explicit.
- Tests assert behavior rather than implementation trivia.
- Functional summary matches observable behavior.
- VERIFY register reflects new external uncertainty.

## Secrets and privacy

- No key/token/authorization header in source, Git, SQLite, browser storage,
  frontend payloads, logs, fixtures, snapshots, exceptions, or test output.
- Browser cannot request/recover secret value.
- Secret injection occurs at infrastructure HTTP boundary.
- Local host binds explicit loopback addresses only by default unless separately
  approved; normal configuration rejects wildcard/LAN listeners.
- Host validation rejects unapproved values/DNS-rebinding-shaped requests.
- Production frontend/API are same-origin and development CORS allows exact
  trusted origins only.
- State-changing local endpoints have cross-origin request/anti-forgery
  protection independent of CORS.
- Private account payloads are minimized/redacted.

## ArenaNet gateway

- Feature code does not construct ArenaNet URLs.
- DTO/domain separation preserved.
- Permission failures explicit.
- Partial/429/transport/invalid-data cases do not masquerade as success.
- Request budget, cancellation, batching, and retry rules preserved.

## Integer money and fees

- Money remains integer copper end-to-end.
- Fee rules centralized.
- Whole-copper rounding checked at boundaries/small values.
- Quantity multiplication overflow considered.
- Gross/net/profit/ROI denominators are consistent.
- Browser does not implement competing formula.

## Accounting

- Completed transactions are authoritative inputs.
- FIFO order deterministic and tie behavior defined.
- Partial lots and one-to-many/many-to-one matching work.
- Unknown prior acquisition stays unknown.
- Realized and unrealized P&L separated.
- Rebuild is deterministic/idempotent.

## Persistence and sync

- Migrations have fresh DB and upgrade coverage.
- Unique external IDs/account scope enforced.
- Writes are transaction-safe where partial state would be harmful.
- Partial remote failure cannot wipe last-known-good data.
- Repeated sync does not duplicate completed history.
- Aging remote history cannot delete local completed history.
- Backup/restore covers newly durable data.

## Market history/statistics

- Observation timestamps/freshness semantics explicit.
- Failed/partial capture does not create fake observation.
- Requested windows have sufficient coverage/sample checks.
- Missing/irregular samples handled deterministically.
- Statistical ratio precision/rounding documented.
- Charts/results expose window and sample count.
- No prediction/guarantee language.

## Opportunity score and risk

- Components are named/decomposable.
- Raw ROI cannot dominate near-zero liquidity by accident.
- Normalization/weights bounded and deterministic.
- Insufficient history reduces confidence.
- Existing orders/positions count toward exposure.
- Cash reserve cannot be allocated.
- Illiquid sizing is constrained by depth/participation.
- Max bid is never exceeded by recommendation composition.

## Recommendation actions

- Action reasons match the actual computed state.
- Outbid does not automatically imply update/cancel.
- One-copper sell undercut does not automatically imply relist.
- WAIT/REVIEW/SKIP exist for insufficient/contradictory evidence.
- No action path executes a Trading Post mutation.

## Crafting

- Owned tradable material has opportunity cost.
- Mixed owned/purchased quantities exact.
- Bound/non-tradable/unknown input states explicit.
- Recipe cycles/depth/candidate limits handled.
- Output liquidity/history considered before calling margin actionable.
