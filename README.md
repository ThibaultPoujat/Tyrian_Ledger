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
The remaining Qwen/MTPLX records are historical snapshots, not active
instructions.

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

### Backend

- Build: `dotnet build TyrianLedger.slnx`
- Test: `dotnet test`
- Run locally: `dotnet run --project src/Gw2Tp.Web`

### Frontend

- Install: `cd frontend && npm ci`
- Dev server: `cd frontend && npm run dev`
- Build: `cd frontend && npm run build`

### Quick local review

Start any missing local services and open the application in a browser:

```bash
./scripts/dev-up.sh
```

The script waits for the API at `http://127.0.0.1:5000/healthz` and the
frontend at `http://127.0.0.1:5173`, reusing a healthy service if it is already
running. It writes local logs and process IDs under ignored `.local/dev/`.

Stop only services started by that script:

```bash
./scripts/dev-down.sh
```

### E2E

The Playwright project is intentionally introduced by the relevant ticket/milestone rather than required for every local development session.
