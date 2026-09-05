# Milestone Context - M18: Owned Historical Market Dataset

## User outcome

Tyrian Ledger starts building its own durable market evidence automatically
while running.

## Sampling priority

Personal orders/positions -> watchlist/high-interest -> broad tradable universe;
full books are more selective than best-price snapshots.

## Invariants

Request budget/backoff/cancellation respected. Failed/partial reads create no
fake observation. UTC/integer-copper persistence. Storage growth and retention
are explicit. Market history is covered by backup.

## Operational handoff

After TKT-M18-02 merges, run the collector during later development whenever
practical so time spent building M19+ also accumulates data.
