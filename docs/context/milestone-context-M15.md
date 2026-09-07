# Milestone Context - M15: Trustworthy Accounting

## User outcome

The application can explain realized profit, fees, open cost basis, current net
value, and 7/30/90-day performance from stored personal transactions.

## Invariants

Canonical fees are centralized. TKT-M15-01 retains VERIFY-013 as OPEN because
the available external sources do not define fractional-copper rounding; the
centralized per-fee round-up behavior and all derived results therefore remain
explicitly modeled/provisional. Integer copper only. FIFO is the initial
accepted lot policy. Partial matches work. Unknown historical basis is not zero.
Realized and unrealized P&L never mix.

## Review

All M15 tickets are R3. Review-model and Draft-state requirements follow the
active Sol gate in `docs/workflow/model-effort-guide.md`; TKT-M15-01 is
SOL-GATED and requires fresh separate Sol XHigh approval.

## Exit

Accounting rebuild is deterministic and sufficiently trustworthy for dashboard,
risk, and recommendation use.
