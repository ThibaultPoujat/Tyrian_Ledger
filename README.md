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

### .NET tests and backend

- Build: `dotnet build TyrianLedger.slnx`
- Unit tests: `dotnet test TyrianLedger.slnx --configuration Release --filter "FullyQualifiedName!~Gw2Tp.IntegrationTests"`
- Integration tests: `dotnet test tests/Gw2Tp.IntegrationTests/Gw2Tp.IntegrationTests.csproj --configuration Release`
- Full .NET suite: `dotnet test TyrianLedger.slnx --configuration Release`
- Run locally: `dotnet run --project src/Gw2Tp.Web`

For the credential boundary and the temporary Development/Testing environment
override, see [local secrets](docs/development/local-secrets.md). A real GW2
credential must never be committed or placed in browser storage.

The API and development frontend bind to loopback by default. See
[local runtime](docs/development/local-runtime.md) for binding overrides and
the HTTP security baseline.

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

## Back up and restore local data

Tyrian Ledger keeps its local preference profile, recorded operation history,
and public market snapshot history in the SQLite file
`user-session-preferences.db`. By default, the file is in the platform's .NET
local application-data directory under `TyrianLedger`; a user or developer may
instead configure its exact location with
`UserSessionPreferences__DatabasePath`.

To create a backup, stop Tyrian Ledger first, then copy that SQLite file to a
backup location you control. Do not copy it while the application is running.
To restore a backup, stop Tyrian Ledger, keep a safety copy of the current
database if wanted, replace `user-session-preferences.db` with the backup, and
start the application again. It applies any required schema migrations at
startup.

Account snapshots and public market cache entries are held only in process
memory, so they are not included in the SQLite backup and disappear when the
application stops. Public market snapshot history is durable local data and is
included in the backup; clearing account snapshots does not remove it. The Guild
Wars 2 API credential is stored separately by the operating system and is not
included in this database; see
[local secrets](docs/development/local-secrets.md). Creating, restoring, or
clearing local data never uploads it. Automated cloud backup is not provided.

## Historical market storage planning

Historical collection is opt-in and will never collect untracked items. The
initial policy supports at most 25 locally tracked items: watchlist items use
hourly top-of-book samples and daily full order-book samples; lower-interest
background items use daily and weekly samples respectively. Snapshot history is
append-only and retained locally until the user removes the database.

The local collector batches due IDs into at most one prices request and one
listings request per cycle, both through the shared GW2 gateway. It rechecks
the opt-in watchlist every minute and pauses for five minutes after a terminal
rate-limit result. These local safeguards are configurable under
`MarketHistory:Collection`; they supplement, and never replace, the gateway's
configurable request scheduler.

For 25 watchlist items, a 365-day planning year yields 219,000 top-of-book
samples and 9,125 order-book samples. Assuming an average of 40 stored levels
per order book and allowing 10% for indexes and SQLite overhead, plan for about
50 MiB per year. This is an estimate rather than a storage guarantee: actual
growth varies with order-book depth and the chosen watchlist mix.
