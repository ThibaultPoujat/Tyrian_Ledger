# ADR-003 - No Application LLM in Current Scope

## Status
Accepted

## Decision
The application contains no LLM integration in the initial architecture. Codex
is a development agent only.

## Rationale
Financial calculations and API semantics must remain deterministic, testable, reproducible, and independent of model behavior.

## Future
A future LLM may explain structured results through an adapter without accessing secrets or executing actions.
