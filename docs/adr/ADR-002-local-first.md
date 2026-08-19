# ADR-002 - Local-First Deployment

## Status
Accepted

## Decision
V1 runs entirely on the user's Mac with a loopback server and SQLite. No cloud server is required.

## Consequences
Simpler security and privacy model; backups are the user's responsibility; future multi-user deployment will require a separate architecture review.
