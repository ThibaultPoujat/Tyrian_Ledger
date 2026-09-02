# Permanent Context

## Identity

This is a static Guild Wars 2 Trading Post analysis application. Codex is a
development tool only; it is not part of the application runtime.

## Hard constraints

- Read-only application.
- No gameplay automation.
- No Trading Post automation.
- No API mutation/write operations.
- No credential or token value in source code, browser code, logs, fixtures, prompts, or tests.
- Core financial truth must be deterministic.
- All GW2 API access goes through one typed gateway with caching/rate limiting.
- Money uses integer copper.
- Tests are required for business logic changes.
- Never invent undocumented GW2 API fields, permissions, quotas, endpoints, or behavior.
- Preserve modular boundaries.
- Do not add an application LLM.

## Target stack

- .NET 10 LTS generator and calculation libraries
- React + TypeScript
- versioned public market snapshots
- xUnit
- Playwright for browser tests

## Local development

The owner's primary development environment is macOS Apple Silicon. The local
Web API and browser client must remain usable on macOS, Windows, and supported
Linux desktop environments; browser clients never depend on a host-specific
secret API. Use a current editor and Codex; the application has no dependency
on a local coding-model runtime.

## Current architecture

Scheduled generator -> Application services -> Analytics/typed GW2 gateway -> public market snapshot.

Browser -> static React assets plus the published market snapshot.

## Lightweight agent workflow

Load the minimum context needed for the current ticket:

1. `AGENTS.md`;
2. this file;
3. the current milestone context;
4. `docs/verification/VERIFY-REGISTER.md`;
5. the assigned ticket under `docs/milestones/<M>/tickets/`;
6. only specialized documents or source files explicitly required by the ticket.

Do not read the entire project specification for routine work.

## Session philosophy

A ticket is a work item, not a single conversation.

A ticket MAY be completed through several fresh sessions, typically:

- implementation;
- tests;
- review and final validation.

Use Git commits and the current working tree as the durable hand-off. Do not depend on the previous chat history.

Use a fresh task when the phase changes, the task has compacted, or a separate
review would add useful independence. Do not run overlapping edits in the same
worktree.

## Execution discipline

For each session:

- inspect the current repository state;
- make a brief plan of no more than five steps;
- execute one coherent work slice;
- validate it;
- review the diff;
- stop.

Prefer execution over repeated summaries.

Do not repeatedly reread unchanged files or repeat an investigation without new evidence.

## VERIFY versus BLOCKED

When uncertain, use `VERIFY` rather than inventing a fact.

`VERIFY` means work can continue safely without treating the fact as true.

`BLOCKED` means missing or contradictory information makes the requested work technically impossible or unsafe.

Only stop for a real blocker. Do not repeatedly investigate a VERIFY item after sufficient evidence has been recorded.

## Required behavior

Prefer small, reversible changes.

Never claim completion without checking the affected acceptance criteria and relevant validation.

## VERIFY register

`docs/verification/VERIFY-REGISTER.md` is the authoritative project-level index of unresolved verification items.

Every ticket session must:

- review relevant existing VERIFY items;
- add newly discovered material VERIFY items;
- update existing items affected by new evidence;
- mark items RESOLVED only when sufficient evidence is recorded in the ticket or another authoritative project document;
- preserve resolved entries for audit/history;
- reference relevant VERIFY IDs in the session or ticket report.
