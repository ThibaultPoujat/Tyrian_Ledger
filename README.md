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
- `docs/workflow/ai-development-workflow.md` - human-readable Qwen workflow overview.
- `docs/workflow/agent-execution-rules.md` - lightweight agent execution and anti-loop rules.
- `docs/workflow/delivery-protocol.md` - Git/GitHub delivery rules.
- `docs/adr/` - Architecture Decision Records (ADRs).
- `docs/milestones/` - milestone definitions and completion criteria.
- `docs/context/` - lightweight context files intended for Qwen.
- `docs/tickets/` - implementation tickets.
- `docs/prompts/` - one lightweight execution prompt per ticket plus the canonical prompt template.
- `config/` - repository configuration templates.
- `.github/pull_request_template.md` - standard ticket PR structure.

## How to use this package

1. Create or clone the Git repository.
2. Read `docs/specs/project-spec.md` once yourself.
3. Start a **fresh Qwen session for each ticket**.
4. Let `config/AGENTS.md` load the permanent context, current milestone, VERIFY register, ticket, and ticket prompt.
5. Let Qwen load specialized documents only when the current ticket explicitly needs them.
6. Execute tickets in dependency order within each milestone.
7. Require tests for behavior changes and direct documentation validation for documentation-only tickets.
8. Do not let Qwen silently change architectural decisions. If a ticket exposes a real architectural issue, update or create an ADR first.
9. Keep the application fully read-only and local until a future scope explicitly changes that decision.
10. For every ticket, use a dedicated branch, prefix ticket commits with `[TICKET_NAME]`, create the required GitHub PR, and never merge the PR as Qwen.

## Normative language

- MUST = mandatory.
- MUST NOT = prohibited.
- SHOULD = strong recommendation; deviation requires rationale.
- MAY = optional.
- ASSUMPTION = provisional until verified.
- VERIFY = must be checked against current authoritative documentation or measured behavior before release.

## Development commands

Requires the .NET 10 SDK and a recent Node.js. No credentials are required for the M1 skeleton.

### Backend

- Build the solution: `dotnet build TyrianLedger.slnx`
- Run all tests: `dotnet test`
- Run the API locally: `dotnet run --project src/Gw2Tp.Web` (URL printed in console output; smoke endpoints: `/` and `/healthz`)

### Frontend

- Install dependencies: `cd frontend && npm install`
- Run the dev server: `cd frontend && npm run dev`
- Production build: `cd frontend && npm run build`

### E2E (stub, later milestones)

- `cd tests/Gw2Tp.Web.E2E && npm install && npx playwright install` then `npm test`

