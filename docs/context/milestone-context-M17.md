# Milestone Context - M17: Live Market Intelligence

## User outcome

The user can find current fee-aware flip candidates and see depth/liquidity risk,
max bid, and safe-size evidence instead of sorting by raw spread/ROI alone.

## Reuse

Prefer adapting the existing public collector, financial calculators, and
order-book simulator over new parallel implementations.

## Invariants

Exact backend economics. Detailed books for shortlisted candidates. Shallow
one-level prices cannot masquerade as liquidity. Scanner is current evidence,
not historical confidence yet.

## Exit

Scanner/watchlist are usable and provide the high-interest market set for M18
history collection.
