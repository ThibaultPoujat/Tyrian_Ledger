# Context - M3

## Milestone
M3 - Deterministic market engine

## Goal
Build deterministic, testable market-profit calculations and opportunity analysis from verified market data.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M3 ticket, and its matching M3 prompt. Read finance-related specs and test guidance only when needed.

## Session rule
Separate calculator implementation, test expansion, and review into fresh sessions when the context grows. Keep active context near 16K. Git commits are the hand-off.

## Rules
Money remains integer copper. Do not mix transport DTOs with domain models. Keep algorithms deterministic, explainable, and bounded by the ticket.
