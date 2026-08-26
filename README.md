# Tyrian Ledger - Qwen/MTPLX Development Package

This repository is the planning and development package for a local, read-only Guild Wars 2 Trading Post analysis application.

## Purpose

Build a browser-based local application that:

- reads Guild Wars 2 public market data and, optionally, account data through the GW2 API;
- minimizes API requests through batching, caching, deduplication, and scheduled refreshes;
- never automates gameplay, Trading Post actions, or API mutations;
- calculates market and crafting opportunities deterministically;
- makes assumptions, data age, uncertainty, liquidity, and opportunity cost visible;
- persists local user history without requiring a server or cloud account;
- may later support historical analysis and long-term investment research;
- may later add an application-facing LLM, but no LLM integration is part of the MVP or current architecture.

## Development environment

The local coding agent is Qwen3.8-27B operated through Pi/MTPLX on an Apple Silicon Mac. MTPLX is a development dependency only and is not part of the application runtime.

## Documentation layout

- `docs/specs/` - normative product/system specifications.
- `docs/architecture/` - technical architecture and API endpoint matrix.
- `docs/security/` - security and French/EU data-protection baseline.
- `docs/testing/` - testing strategy.
- `docs/ux/` - UI/UX rules.
- `docs/adr/` - Architecture Decision Records.
- `docs/context/` - lightweight context intended for the coding agent.
- `docs/verification/` - unresolved external-contract and verification register.
- `docs/milestones/` - milestone definitions plus milestone-scoped `tickets/` and `prompts/`.
- `docs/workflow/` - coding-agent execution and delivery rules.
- `config/AGENTS.md` - agent entry rules loaded by Pi.

## Agent workflow

1. Start a fresh Pi session for each coherent ticket phase.
2. Load only the permanent context, current milestone context, VERIFY register, one ticket, and the matching prompt.
3. Read specialized documents or source files only when required.
4. Keep the active local context around 16K tokens by default.
5. Use Git commits and the working tree as the hand-off between sessions.
6. Split complex tickets into sequential implementation/test/review sessions instead of preserving one large conversation.
7. Complete Git/GitHub delivery through the repository delivery protocol; Qwen must not merge its own PR.

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

- Build: `dotnet build TyrianLedger.slnx`
- Test: `dotnet test`
- Run locally: `dotnet run --project src/Gw2Tp.Web`

### Frontend

- Install: `cd frontend && npm install`
- Dev server: `cd frontend && npm run dev`
- Build: `cd frontend && npm run build`

### E2E

The Playwright project is intentionally introduced by the relevant ticket/milestone rather than required for every local development session.
