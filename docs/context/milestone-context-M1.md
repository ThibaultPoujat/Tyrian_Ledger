# Context - M1

## Milestone
M1 - Repository and development foundation

## Goal
Create the solution, modules, tests, CI-friendly commands, local configuration, and secure local runtime skeleton.

## Agent context
Load `docs/context/permanent-context.md`, the VERIFY register, one M1 ticket, and its matching M1 prompt. Read deeper documents only when required.

## Session rule
M1 tickets may use multiple fresh sessions: implementation, tests, and review/validation can be separate sessions. Use Git state as the hand-off and keep active context around the local 16K target.

## Rules
Implement only the assigned M1 ticket. If a ticket exposes a durable architecture change, create/update an ADR before coding beyond the smallest safe change. Do not start another ticket from the same session.
