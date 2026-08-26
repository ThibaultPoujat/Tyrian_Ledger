# Context - M5

## Milestone
M5 - Account-aware analysis and crafting

## Goal
Add authorized account-aware analysis, inventory/bank opportunity cost, and bounded crafting-path evaluation.

## Agent context
Load `AGENTS.md`, `docs/context/permanent-context.md`, the VERIFY register, and one M5 ticket. Read account/API permissions, crafting, security, and relevant test guidance only when required.

## Session rule
Prefer separate tasks for account data access, analytics/crafting logic, tests, and security review. Use Git state as the hand-off.

## Rules
Never expose API keys or account payloads unnecessarily. Owned materials are not free: account-aware recommendations must model opportunity cost. Keep crafting search bounded and deterministic.
