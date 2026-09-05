# Clean-Checkout Validation

## Purpose

This is the active validation procedure for Tyrian Ledger after the M12
static-runtime retirement. Run it from a clean checkout before reporting an
implementation ticket complete or treating `develop` as a known-good baseline.

The commands exercise only local code and synthetic fixtures. They do not need
an ArenaNet API key and do not perform Guild Wars 2 or Trading Post mutations.

## Prerequisites

- Git with the repository's complete history available for the secret scan;
- .NET 10 SDK;
- Node.js 22 and npm;
- Gitleaks;
- network access for the two locked `npm ci` installs and the initial
  Playwright browser download.

Start from a fresh clone or clean worktree at the revision being validated. To
validate the shared baseline, use the current `origin/develop` revision and
confirm that this command prints nothing:

```bash
git status --short
```

## Sequential validation matrix

Run these commands in order from the repository root. Do not reinstall or build
the frontend concurrently with Playwright: the browser suite builds and serves
the same frontend directory.

### .NET solution

```bash
dotnet restore TyrianLedger.slnx
dotnet build TyrianLedger.slnx --no-restore --configuration Release
dotnet test TyrianLedger.slnx --no-build --configuration Release --verbosity normal
```

### React frontend

```bash
cd frontend
npm ci
npm test
npm run build
cd ..
```

### Browser E2E

```bash
cd tests/Gw2Tp.Web.E2E
npm ci
npx playwright install chromium firefox webkit
npm test
cd ../..
```

The Playwright command runs the active transition-shell checks in Chromium,
Firefox, and WebKit. The suite must not be reduced to one browser when claiming
the complete baseline.

### CI contracts and retired-runtime audit

```bash
node --test .github/scripts/*.test.mjs
git grep -n -I -E 'Gw2Tp\.MarketSnapshotGenerator|pages-snapshot-scheduler|pages\.yml|staticSnapshot|marketSnapshot' -- TyrianLedger.slnx .github/workflows .github/scripts frontend src tests
```

The workflow-contract suite verifies that every active GitHub Action remains
pinned, the generic secret/.NET/frontend/browser validation jobs remain in CI,
and the retired Pages workflow and selector are absent. The `git grep` audit is
expected to find only negative assertions in
`.github/scripts/workflow-contract.test.mjs`; any match in the solution, active
workflow, package/source, or active test paths must be investigated.

Historical M10-M11 documentation and superseded ADRs deliberately remain as
project history and are not active-runtime matches.

### Full-history secret scan

```bash
gitleaks detect --source . --log-opts="--all" --redact --no-banner
```

Do not run this from a shallow clone. The scan passes only when Gitleaks reports
no leaks in the complete reachable history.

### Final repository checks

```bash
git diff --check
git status --short
```

Review the full ticket diff as well as these mechanical checks. Generated
`bin`, `obj`, `dist`, `node_modules`, Playwright report, and test-result paths
are ignored; unexpected tracked or untracked files must be investigated before
delivery.

## M12 known-good checkpoint

On 2026-09-05, the clean post-retirement `develop` revision
`c1153b7a0d72086f011355e3e830ed27cbcaef3a` passed this matrix with:

- a Release solution build with zero warnings and zero errors;
- 102 .NET tests;
- 2 frontend component tests and a production frontend build;
- 9 Playwright tests across Chromium, Firefox, and WebKit;
- 3 CI workflow-contract tests; and
- a full-history Gitleaks scan of 163 commits with no leaks.

The ticket branch and its pull request must rerun the same matrix so their
additional documentation commit and final CI state are also covered.
