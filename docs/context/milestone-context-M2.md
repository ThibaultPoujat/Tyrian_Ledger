# Context - M2

## Milestone
M2 - GW2 data gateway and caching

## Goal
Implement the verified, read-only GW2 data gateway with request minimization, caching, rate-limit handling, and resilient external-data mapping.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M2 ticket, and its matching M2 prompt. Read the endpoint matrix, rate-limit policy, architecture, and testing strategy only when required.

## Session rule
M2 tickets should be completed through small sessions where practical: gateway slice, focused tests, then review/validation. Keep active context near 16K and use Git state as the hand-off.

## Rules
Only use verified API contracts. Route all GW2 access through the gateway. Never add mutation/write operations. Do not proceed on unresolved external facts as though they were true.
