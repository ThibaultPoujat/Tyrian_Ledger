# Context - M0

## Milestone
M0 - Discovery and external-contract validation

## Goal
Establish the external-contract, security, compatibility, and architectural assumptions required before feature implementation.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M0 ticket, and its matching M0 prompt. Read deeper documents only when the current ticket needs them.

## Session rule
M0 tickets may use multiple fresh sessions. Prefer short verification/documentation slices over a long conversation. Treat the repository and Git state as the hand-off.

## Rules
Implement only the assigned M0 ticket. Do not invent external API/legal facts. Record material uncertainty as VERIFY and stop only for a real BLOCKED condition. If a ticket is complete for the current session slice, stop rather than starting the next ticket.
