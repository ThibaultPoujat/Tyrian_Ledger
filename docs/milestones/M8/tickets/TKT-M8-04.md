# TKT-M8-04 - Prepare local release package and handoff documentation

## Milestone
M8

## Goal
Make the project reproducible for the owner.

## Dependencies
M8-01,M8-02,M8-03

## Acceptance criteria
- [x] One documented setup path works on a clean Mac environment.
- [x] One documented run/test command set exists.
- [x] Configuration and backup instructions are complete.
- [x] Known limitations and deferred features are listed.
- [x] No secrets or private data are included in the release package.

## Required tests
- [x] Clean-environment checklist.
- [x] Release smoke test.

## Non-goals
- Public deployment.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.

## Completion evidence (2026-08-29)

- `docs/development/local-source-release.md` is the canonical source-handoff
  path for a clean macOS clone: .NET 10 and Node.js 22 prerequisites, locked
  dependency installs, loopback-only two-process run, full tests, optional
  Keychain credential handling, SQLite backup/restore, and the handoff
  integrity checklist. `README.md` now points to it instead of duplicating
  operational instructions.
- The committed guide was exercised from a fresh detached worktree using .NET
  10 and Node.js 22: `dotnet restore TyrianLedger.slnx`, both `npm ci`
  commands, and `npx playwright install chromium firefox webkit` completed.
  The full .NET suite passed (4 Domain, 45 Application, 58 Analytics, 86
  Infrastructure, and 27 Integration tests); the frontend suite passed (15
  tests) and production build completed; Playwright passed all 21 Chromium,
  Firefox, and WebKit tests.
- An isolated no-credential API smoke using a temporary SQLite database
  returned `"ok"` from `/healthz` and `{"credentialStatus":"not-configured"}`
  from `/api/status`. Gitleaks scanned the source with `.gitleaks.toml` and
  reported no leaks.
- This is a source-only local handoff, not a distributable, public deployment,
  or formal external-API release sign-off. No public API, schema, runtime
  behavior, or VERIFY status changed. `VERIFY-004`, `VERIFY-005`,
  `VERIFY-006`, `VERIFY-007`, `VERIFY-008`, `VERIFY-010`, `VERIFY-011`, and
  `VERIFY-012` remain explicitly carried forward and require verification
  before a release that depends on those external contracts.
