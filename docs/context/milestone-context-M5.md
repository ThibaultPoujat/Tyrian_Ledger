# Context - M5

## Milestone
M5 - Account-aware analysis and crafting

## Goal
Add authorized account-aware analysis, inventory/bank opportunity cost, and bounded crafting-path evaluation.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M5 ticket, and its matching M5 prompt. Read account/API permissions, crafting, security, and relevant test guidance only when required.

## Session rule
Prefer separate sessions for account data access, analytics/crafting logic, tests, and security review. Keep active context near 16K and use Git state as the hand-off.

## Rules
Never expose API keys or account payloads unnecessarily. Owned materials are not free: account-aware recommendations must model opportunity cost. Keep crafting search bounded and deterministic.
