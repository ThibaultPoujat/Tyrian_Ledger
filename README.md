# Tyrian Ledger

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

## Development with Codex

Use Codex with GPT-5.6 Terra at High reasoning effort for normal ticket work.
Use a separate XHigh review task for security-sensitive, financial,
architectural, or unusually difficult work. The coding agent is a development
tool only; it is not part of the application runtime.

Start with [AGENTS.md](AGENTS.md), then follow
[the owner/Codex collaboration guide](docs/workflow/codex-collaboration.md).
The committed development-package PDF and its Qwen/MTPLX records are historical
snapshots, not active instructions.

## Documentation layout

- `docs/specs/` - normative product/system specifications.
- `docs/architecture/` - technical architecture and API endpoint matrix.
- `docs/security/` - security and French/EU data-protection baseline.
- `docs/testing/` - testing strategy.
- `docs/ux/` - UI/UX rules.
- `docs/adr/` - Architecture Decision Records.
- `docs/context/` - focused context for the current ticket.
- `docs/verification/` - unresolved external-contract and verification register.
- `docs/milestones/` - milestone definitions and ticket contracts.
- `docs/workflow/` - coding-agent execution and delivery rules.
- `AGENTS.md` - Codex entry instructions.

## Agent workflow

1. Give Codex a functional brief for one small outcome.
2. Codex works from one ticket and the minimum relevant context.
3. Use a clean implementation task followed by a fresh review task.
4. Use commits, tests, tickets, and pull requests as the hand-off.
5. The owner confirms functional success and merges after CI passes.

## Normative language

- MUST = mandatory.
- MUST NOT = prohibited.
- SHOULD = strong recommendation; deviation requires rationale.
- MAY = optional.
- ASSUMPTION = provisional until verified.
- VERIFY = must be checked against current authoritative documentation or measured behavior before release.

## Development commands

Requires the .NET 10 SDK and a recent Node.js. No credentials are required for the M1 skeleton.

### .NET tests and backend

- Build: `dotnet build TyrianLedger.slnx`
- Unit tests: `dotnet test TyrianLedger.slnx --configuration Release --filter "FullyQualifiedName!~Gw2Tp.IntegrationTests"`
- Integration tests: `dotnet test tests/Gw2Tp.IntegrationTests/Gw2Tp.IntegrationTests.csproj --configuration Release`
- Full .NET suite: `dotnet test TyrianLedger.slnx --configuration Release`
- Run locally: `dotnet run --project src/Gw2Tp.Web`

For the credential boundary and the temporary Development/Testing environment
override, see [local secrets](docs/development/local-secrets.md). A real GW2
credential must never be committed or placed in browser storage.

### Frontend

- Install: `cd frontend && npm ci`
- Dev server: `cd frontend && npm run dev`
- Build: `cd frontend && npm run build`

### E2E

The Playwright suite starts the loopback-only ASP.NET Core API and Vite frontend
automatically. It uses the local health endpoint and the React shell only; it
does not call the live GW2 API.

- Install frontend dependencies: `cd frontend && npm ci`
- Install E2E dependencies: `cd tests/Gw2Tp.Web.E2E && npm ci`
- Install the Chromium test browser: `cd tests/Gw2Tp.Web.E2E && npx playwright install chromium`
- Run browser smoke tests: `cd tests/Gw2Tp.Web.E2E && npm test`
