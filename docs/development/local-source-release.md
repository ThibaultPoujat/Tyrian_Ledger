# Local source-release handoff

This is the canonical setup, run, validation, configuration, and data-handling
guide for Tyrian Ledger on macOS. It describes a source handoff for one local
user; it is not a packaged application, a public deployment, or a claim that
all external Guild Wars 2 API contracts have been revalidated.

## Scope and prerequisites

Tyrian Ledger is a local, read-only Guild Wars 2 Trading Post analysis tool.
It binds its API and development UI to loopback, does not automate gameplay or
Trading Post actions, and must not be exposed to a LAN or public network.

On a clean Mac, install:

- Git;
- the .NET 10 SDK for the Mac architecture in use. The repository selects
  `10.0.100` in `global.json` and permits later .NET 10 feature-band SDKs;
- Node.js 22 LTS, matching the CI runtime.

Use the official [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
and [Node.js 22 downloads](https://nodejs.org/en/download/archive/v22) for the
appropriate macOS installer. Verify the toolchain before cloning:

```sh
git --version
dotnet --version
node --version
npm --version
```

`dotnet --version` must report a `10.*` SDK and `node --version` must report a
`v22.*` runtime. No Guild Wars 2 credential is required to install, test, or
start the local M9 shell.

## Clean setup and local run

Start from a new clone. Do not copy `.env` files, databases, logs,
`node_modules`, or other local state into it.

```sh
git clone https://github.com/ThibaultPoujat/Tyrian_Ledger.git
cd Tyrian_Ledger
dotnet restore TyrianLedger.slnx
(cd frontend && npm ci)
(cd tests/Gw2Tp.Web.E2E && npm ci && npx playwright install chromium firefox webkit)
```

Run the application in two terminals from the repository root:

```sh
# Terminal 1: local API
dotnet run --project src/Gw2Tp.Web --no-launch-profile

# Terminal 2: local browser client
npm --prefix frontend run dev
```

Open the loopback URL printed by Vite (normally
`http://127.0.0.1:5173`). The API defaults to `http://127.0.0.1:5000`; confirm
it responds with:

```sh
curl --fail http://127.0.0.1:5000/healthz
```

Keep both processes in the foreground and stop them with `Ctrl+C`. See
[local runtime](local-runtime.md) for the loopback-only binding policy and
intentional local port overrides. Do not use `0.0.0.0`, wildcard, LAN, or
public addresses.

## Configuration and local data

M9 reads public market data only and does not require, read, or store a Guild
Wars 2 API credential. If a pre-M9 installation has an old Tyrian Ledger
credential in its operating system's secret store, remove it manually using
the [retired credential cleanup](retired-credential-cleanup.md) guide.

The SQLite database contains only the local preference profile. Its default
location is the .NET local application-data directory under `TyrianLedger` as
`user-session-preferences.db`. To select a known local location, set the
configuration key only for the launch command:

```sh
mkdir -p "$PWD/var"
UserSessionPreferences__DatabasePath="$PWD/var/user-session-preferences.db" \
  dotnet run --project src/Gw2Tp.Web --no-launch-profile
```

`var/` and SQLite files are ignored by Git. The configured directory is
created automatically. Test runs use isolated temporary database locations.

### Back up and restore

1. Stop Tyrian Ledger. Do not copy a SQLite database while it is in use.
2. Copy `user-session-preferences.db` from its default or configured location
   to a backup location under the owner's control.
3. To restore, stop the application, optionally keep a safety copy of the
   current database, replace the database with the backup, then start the
   application. Required schema migrations run at startup.

Public market-cache entries are in-memory only and are not backed up. M9 does
not retain market snapshots, recommendations, partial scans, account data, or
personal history. Backups and restores never upload data. Automated cloud
backup is not provided.

## Test and release-smoke command set

All automated tests use local fixtures and mocks; they must not call the live
Guild Wars 2 API. Run this complete command set before handing off a source
release:

```sh
dotnet test TyrianLedger.slnx --configuration Release
(cd frontend && npm test && npm run build)
(cd tests/Gw2Tp.Web.E2E && npm test)
```

The Playwright command starts a loopback API and Vite server automatically,
then runs the desktop Chromium, Firefox, and WebKit projects. It is the
automated release smoke test; the clean run above also verifies that no
credential reaches browser traffic. A physical Safari check remains a manual
release-stage check when Safari is available.

## Clean-environment checklist

Use this checklist once from a fresh clone before each local handoff:

- [ ] Only a .NET `10.*` SDK and Node.js `v22.*` toolchain were used.
- [ ] Dependency installation completed using the three commands above, with
  no copied local state or credentials.
- [ ] The .NET suite, frontend test/build, and complete Playwright browser
  matrix passed.
- [ ] The two-process local run served `/healthz` on `127.0.0.1:5000` and the
  Recommendations shell loaded at Vite's loopback URL without a credential.
- [ ] The repository contains no tracked `.env`, database, log, credential,
  private account payload, or generated dependency directory.
- [ ] A source scan passed with the repository Gitleaks configuration:

  ```sh
  gitleaks detect --source . --no-git --config .gitleaks.toml
  ```

- [ ] The final `git diff --check` and `git status --short` show only the
  intended handoff documentation changes.

## Limits, deferred work, and verification status

- This is a single-user, desktop-first, loopback-only source handoff. Mobile,
  public/multi-user hosting, TLS/HSTS, cloud persistence, and automated cloud
  backup are outside its scope.
- Future M9 recommendation results are modeled guidance, not guarantees of
  price, liquidity, profit, or execution time. The app never places or cancels
  Trading Post orders and never automates gameplay.
- Current desktop Chrome, Firefox, and Safari are the supported browser
  targets. Playwright covers Chromium, Firefox, and WebKit; physical Safari is
  a manual check when available.
- `VERIFY-004`, `VERIFY-005`, `VERIFY-006`, `VERIFY-007`, `VERIFY-010`,
  `VERIFY-011`, `VERIFY-012`, and `VERIFY-013` remain open in the
  [VERIFY register](../verification/VERIFY-REGISTER.md). They cover external
  endpoint, schema, rate-limit, paging, scope, and response-contract facts.
  They are explicitly carried forward by this local handoff and must be
  revalidated and documented before a formal release that depends on them.

The handoff excludes all ignored local material. Do not create a source archive
from a working directory containing untracked local data; hand off only the
reviewed Git commit or a clean checkout of it.
