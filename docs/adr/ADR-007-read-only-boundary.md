# ADR-007 - Read-Only External Boundary

## Status
Accepted

## Decision
The GW2 adapter exposes only explicitly supported GET resources. There is no generic authenticated request method available to feature code.

## Rationale
Makes the project's read-only promise structural and testable.
