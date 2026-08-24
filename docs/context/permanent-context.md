# Permanent Context for Qwen

## Identity

You are the coding agent for a local Guild Wars 2 Trading Post analysis application.
Qwen is a development tool only; it is not part of the application runtime.

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
- Do not add an application LLM; Qwen is only the development agent.

## Target stack

- ASP.NET Core / .NET 10 LTS
- React + TypeScript
- SQLite
- xUnit
- Playwright for browser tests

## Local development

macOS Apple Silicon. Visual Studio for Mac is retired; use Visual Studio Code or equivalent. MTPLX serves the local coding model on loopback.

## Current architecture

Browser -> Web API -> Application services -> Analytics/Infrastructure -> SQLite/GW2 API.

## Model/runtime note

MTPLX is MLX-native. The raw `unsloth/Qwen3.8-27B-GGUF` model is not assumed to load directly. Compatibility was investigated in M0. The application itself must not depend on MTPLX.

## Agent execution discipline

For a ticket, use the minimum context needed:

1. this file;
2. the current milestone context;
3. `docs/verification/VERIFY-REGISTER.md`;
4. the assigned ticket;
5. the assigned prompt;
6. only specialized documents or source files explicitly required by the ticket.

Do not read the entire project specification unless a specific unresolved requirement cannot be answered from the smaller context.

One ticket is one execution unit. Implement only that ticket and stop when it is complete.

Planning must be brief. Prefer execution over repeated summaries.

Do not repeatedly reread unchanged files or repeat an investigation without new evidence.

## VERIFY versus BLOCKED

When uncertain, use `VERIFY` rather than inventing a fact.

`VERIFY` means work can continue safely without treating the fact as true.

`BLOCKED` means missing or contradictory information makes the requested work technically impossible or unsafe.

Only stop for a blocker. Do not repeatedly investigate a VERIFY item after sufficient evidence has been recorded.

## Required behavior

Prefer small, reversible changes.

Never claim completion without checking the ticket acceptance criteria and relevant validation.

## VERIFY register

`docs/verification/VERIFY-REGISTER.md` is the authoritative project-level index of unresolved verification items.

Every ticket must:

- review relevant existing VERIFY items before implementation;
- add newly discovered material VERIFY items;
- update existing items affected by new evidence;
- mark items RESOLVED only when sufficient evidence is recorded in the ticket or another authoritative project document;
- preserve resolved entries for audit/history;
- reference relevant VERIFY IDs in the ticket report.

The register is an index. Detailed evidence remains in the ticket or another authoritative project document.
