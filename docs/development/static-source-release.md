# Static development handoff

> **Historical / superseded:** This M10-M11 source/release guide is retained as
> evidence only. ADR-010 and the M12 product pivot retire the static Pages
> release path; do not follow it for active development or deployment.
> TKT-M12-02 removes the retired runtime.

Tyrian Ledger is a static, read-only Guild Wars 2 Trading Post analysis site.
The browser loads React assets and a published `market-snapshot.json`; it does
not run or contact a local API. This guide describes local source development,
not a public deployment or a revalidation of external Guild Wars 2 contracts.

## Prerequisites and clean setup

Install Git, a .NET 10 SDK, and Node.js 22 LTS. The repository selects
`10.0.100` in `global.json` and CI uses Node 22.

From a fresh clone, install the checked-in dependencies:

```sh
dotnet restore TyrianLedger.slnx
npm --prefix frontend ci
npm --prefix tests/Gw2Tp.Web.E2E ci
npx --prefix tests/Gw2Tp.Web.E2E playwright install chromium firefox webkit
```

No Guild Wars 2 credential, database, copied local state, or `.env` file is
needed to install or test the static site.

## Static build and preview

Build the browser assets, then preview them locally:

```sh
npm --prefix frontend run build
npm --prefix frontend run preview -- --host 127.0.0.1
```

To exercise the project-Pages asset base locally, add
`VITE_SITE_BASE_PATH=/Tyrian_Ledger/` to the build command. The workflow also
sets a revisioned `VITE_MARKET_SNAPSHOT_PATH` for each generated snapshot; the
browser always keeps that path within the configured static deployment base.

The preview intentionally reports an unavailable snapshot until a
`market-snapshot.json` is available beside the static assets. For a local
fixture preview, copy the synthetic test artifact only into the ignored build
directory after building:

```sh
cp tests/fixtures/market-snapshots/market-snapshot-v1.json frontend/dist/market-snapshot.json
```

Open the preview URL printed by Vite, set capital and risk, and confirm the
recommendations are calculated locally. Reloading retains those settings only
in the browser. Removing the fixture restores the no-recommendations,
unavailable-snapshot state.

## Snapshot generator

The generator is the only local command that contacts the public Guild Wars 2
API. It uses the typed, read-only gateway and writes a complete versioned
artifact atomically to a caller-selected location:

```sh
dotnet run --project src/Gw2Tp.MarketSnapshotGenerator -- --output /path/to/market-snapshot.json
```

It enforces the M10 policy of two requests per second, at most two concurrent
requests, and burst budget 20. A non-zero exit means no complete artifact was
published. Scheduled generation and GitHub Pages publication are owned by
TKT-M10-05; do not treat this local command as a deployment workflow. See the
[GitHub Pages deployment guide](github-pages-deployment.md) for its trusted
workflow, static-artifact validation, and owner-controlled publication gate.

## Validation

All normal tests use fixtures and mocks rather than live Guild Wars 2 traffic:

```sh
dotnet test TyrianLedger.slnx --configuration Release
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix tests/Gw2Tp.Web.E2E test
```

Before handoff, run `git diff --check`, inspect the static build, and verify
the repository contains no tracked secret, database, generated snapshot, or
local dependency directory. The unresolved public API and fee facts remain in
the [VERIFY register](../verification/VERIFY-REGISTER.md).
