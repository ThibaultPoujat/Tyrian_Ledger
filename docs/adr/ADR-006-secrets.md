# ADR-006 - Local Secret Storage

## Status
Accepted

## Decision
API credentials are stored outside source code using an OS-backed secret mechanism.

## Development fallback
An environment variable may be used for local development and test execution only; it must never be committed.
