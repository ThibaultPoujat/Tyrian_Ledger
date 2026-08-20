# Tyrian Ledger - Qwen/MTPLX Development Package

This repository is the complete planning and development package for a local, read-only Guild Wars 2 Trading Post analysis application.

## Purpose

Build a browser-based local application that:

- reads Guild Wars 2 public market data and, optionally, account data through the GW2 API;
- minimizes API requests through batching, caching, deduplication, and scheduled refreshes;
- never automates gameplay, Trading Post actions, or API mutations;
- calculates market and crafting opportunities deterministically;
- makes assumptions, data age, uncertainty, liquidity, and opportunity cost visible;
- persists local user history without requiring a server or cloud account;
- can later support historical analysis and long-term investment research;
- may later add an application-facing LLM, but **no LLM integration is part of the MVP or current architecture**.

## Development environment

The local coding agent is Qwen3.8-27B operated through MTPLX on an Apple Silicon Mac. MTPLX exposes an OpenAI-compatible local API and is used only as the development assistant/runtime. It is not an application runtime dependency.

Important: MTPLX currently runs MLX-native model artifacts and explicitly treats GGUF as the llama.cpp format. The raw `unsloth/Qwen3.8-27B-GGUF` repository must therefore be treated as a source model reference, not as a guaranteed MTPLX-loadable artifact. Milestone M0 contains an explicit compatibility gate before development proceeds.

## Important editor note

Visual Studio for Mac was retired on August 31, 2024. On macOS use Visual Studio Code (or another current editor) with the local MTPLX-compatible coding workflow.

## Deliverables in this package

- `docs/specs/project-spec.md` - normative product and system specification.
- `docs/architecture/architecture.md` - technical architecture.
- `docs/security/security.md` - security and French/EU data-protection baseline.
- `docs/testing/testing-strategy.md` - test strategy and quality gates.
- `docs/ux/ux.md` - UI/UX rules.
- `docs/workflow/ai-development-workflow.md` - development workflow for Qwen via MTPLX.
- `docs/adr/` - Architecture Decision Records (ADRs).
- `docs/milestones/` - milestone definitions and completion criteria.
- `docs/context/` - lightweight context files intended for Qwen, including Git/PR delivery rules.
- `docs/tickets/` - implementation tickets.
- `docs/prompts/` - one execution prompt per ticket.
- `config/` - repository configuration templates.
- `.github/pull_request_template.md` - standard ticket PR structure.

## How to use this package

1. Create an empty Git repository.
2. Copy the contents of this package into it.
3. Read `docs/specs/project-spec.md` once yourself.
4. Load only `docs/context/permanent-context.md`, the current milestone context, and one ticket into Qwen at a time.
5. Execute tickets in numerical order within each milestone unless an explicit dependency says otherwise.
6. Require tests for every behavior change.
7. Do not let Qwen silently change architectural decisions. If a ticket exposes a real architectural issue, update or create an ADR first.
8. Keep the application fully read-only and local until a future scope explicitly changes that decision.
9. For every ticket, use a dedicated branch, prefix every ticket commit with `[TICKET_NAME]`, create a GitHub PR titled `[TICKET_NAME] Short title`, include exact specification references in the PR body, and never merge the PR as Qwen.

## Normative language

- MUST = mandatory.
- MUST NOT = prohibited.
- SHOULD = strong recommendation; deviation requires rationale.
- MAY = optional.
- ASSUMPTION = provisional until verified.
- VERIFY = must be checked against current authoritative documentation or measured behavior before release.
