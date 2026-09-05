# Milestone Context - M15: Trustworthy Accounting

## User outcome

The application can explain realized profit, fees, open cost basis, current net
value, and 7/30/90-day performance from stored personal transactions.

## Invariants

Canonical fees are centralized. They become externally verified only if
TKT-M15-01 records sufficient authoritative rounding evidence and resolves
VERIFY-013; otherwise the centralized behavior and all derived results remain
explicitly modeled/provisional. Integer copper only. FIFO is the initial
accepted lot policy. Partial matches work. Unknown historical basis is not zero.
Realized and unrealized P&L never mix.

## Review

All M15 tickets are R3 and require fresh flagship XHigh financial review.

## Exit

Accounting rebuild is deterministic and sufficiently trustworthy for dashboard,
risk, and recommendation use.
