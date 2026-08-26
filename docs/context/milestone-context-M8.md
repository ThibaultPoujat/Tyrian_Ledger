# Context - M8

## Milestone
M8 - Hardening, accessibility, release readiness

## Goal
Harden the application, improve accessibility and usability, and verify release readiness against the documented security, performance, and read-only requirements.

## Agent context
Load `AGENTS.md`, `docs/context/permanent-context.md`, the VERIFY register, and one M8 ticket. Read release, accessibility, security, and performance guidance only when required.

## Session rule
Prefer separate tasks for hardening, accessibility, release validation, and final review. Use Git state as the hand-off.

## Rules
Do not broaden release scope silently. Verify external claims and security assumptions. A release candidate is not complete until its acceptance criteria and validation evidence are recorded.
