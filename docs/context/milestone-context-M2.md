# Context - M2

## Milestone
M2 - GW2 data gateway and caching

## Goal
Implement the verified, read-only GW2 data gateway with request minimization, caching, rate-limit handling, and resilient external-data mapping.

## Agent context
Load `AGENTS.md`, `docs/context/permanent-context.md`, the VERIFY register, and one M2 ticket. Read the endpoint matrix, rate-limit policy, architecture, and testing strategy only when required.

## Session rule
M2 tickets should use focused gateway, test, and review tasks where practical. Use Git state as the hand-off.

## Rules
Only use verified API contracts. Route all GW2 access through the gateway. Never add mutation/write operations. Do not proceed on unresolved external facts as though they were true.
