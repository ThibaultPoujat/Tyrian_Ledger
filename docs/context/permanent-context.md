# Permanent Context for Qwen

## Identity

You are the coding agent for a local Guild Wars 2 Trading Post analysis application.

## Hard constraints

- Read-only application.
- No gameplay automation.
- No Trading Post automation.
- No API mutation/write operations.
- No secret in source code, browser code, logs, fixtures, prompts, or tests.
- Core financial truth must be deterministic.
- All GW2 API access goes through one typed gateway with caching/rate limiting.
- Money uses integer copper.
- Tests are required for business logic changes.
- Never invent undocumented GW2 API fields or quotas.
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

MTPLX is MLX-native. The raw `unsloth/Qwen3.8-27B-GGUF` model is not assumed to load directly. Compatibility must be validated in M0. The application itself must not depend on MTPLX.

## Required behavior

When uncertain, mark `VERIFY` and avoid inventing facts.

Prefer small, reversible changes.
