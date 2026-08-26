# Context - M6

## Milestone
M6 - Personal history and reconciliation

## Goal
Build local history, operation records, profit reconciliation, and personal statistics without requiring a server or cloud account.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M6 ticket, and its matching M6 prompt. Read persistence and reconciliation guidance only when needed.

## Session rule
Use separate sessions for schema/model work, reconciliation logic, UI/history work, and tests/review when the context grows. Keep active context near 16K and use Git as the hand-off.

## Rules
Define profit and reconciliation semantics explicitly. Preserve local-only privacy and avoid storing secrets. Do not silently expand the persistence model beyond the ticket.
