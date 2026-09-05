# Milestone Context - M16: Personal Dashboard and Current Orders

## User outcome

The first post-pivot UI is already useful: account/sync health, data coverage,
7/30/90 results, open exposure, current orders, and recent trades are visible
without spreadsheets.

## Invariants

React renders backend-authoritative values. Unknown coverage/basis is visible.
No scanner/recommendation scope creep. API key never enters browser payloads.

## Exit

A non-programmer can inspect personal trading state and verify that persisted
accounting behaves plausibly before advanced recommendation work begins.
