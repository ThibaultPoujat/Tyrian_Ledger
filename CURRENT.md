# Current Project State

Last updated: 2026-09-06

## Active direction

Tyrian Ledger is pivoting from the M10-M11 public static Pages product into a
**local-first personal Guild Wars 2 Trading Post assistant**.

The owner has approved the product vision and the architectural direction:

`React UI -> loopback ASP.NET Core host/API -> deterministic application logic -> SQLite + typed read-only ArenaNet API gateway`

The existing C# financial/domain/API foundation and useful React/test work are
to be preserved. Static Pages publishing, the external Pages scheduler, the
public market-snapshot runtime, and browser-side duplicate authoritative
recommendation calculations are transition code retired in M12 rather than new
architecture to extend.

## Active milestone

**M14 - Durable Personal Data**

M13 is complete through the local host, secure API-key validation, and typed
personal Trading Post gateway work merged in PRs #102-#105.

M14 is complete in implementation branches: PR #106 established versioned
SQLite persistence, PR #107 added idempotent personal TP synchronization, and
TKT-M14-03 / #74 adds local backup, guarded restore, and explicit
clear-personal-data controls. The database remains local-only; API keys stay
outside SQLite, and backups never upload automatically.

The current handoff is **TKT-M14-03 / #74**. It follows the NORMAL quota-aware
review path and must receive its independent Terra review plus green validation
before owner merge. After that merge, the next valid implementation ticket is:

**TKT-M15-01 / #75 - Establish and Register the Canonical GW2 Trading Post Fee Policy.**

## Known-good baseline

TKT-M14-01 validation on commit
`119ff69820a2bf20381d543595ed40194a7ca67f` reported:

- Release solution build: zero warnings and zero errors;
- .NET: 177 tests passed;
- React: 5 component tests passed and the production build succeeded;
- Playwright: 9 tests passed across Chromium, Firefox, and WebKit;
- CI workflow contracts: 3 tests passed;
- Gitleaks: 174 reachable commits scanned with no leaks.

PR #106 is merged into `develop` as merge commit
`e30d9b1241e3a042ebd529817661057417d1a596`.

## Important transition warning

The active repository shape no longer builds or deploys the old static Pages
delivery, external scheduler, publishable market snapshot, or browser-side
recommendation formulas. Those concepts remain only in explicitly superseded
historical records, not as permission to restore the public runtime.

Do not delete proven Domain, Analytics, typed gateway, request scheduler,
order-book simulator, fixtures, or tests merely because they were used by the
static product. Reuse them in the local-first architecture where compatible.

## Product checkpoints

- M15: trustworthy personal accounting foundation.
- M16: first useful personal dashboard.
- M17: usable live market scanner.
- M18: owned historical market collection is running.
- M19: primary `What Should I Do?` recommendation product is usable.
- M20: personal performance learning and investment tracking.
- M21: crafting intelligence.
- M22: alerts, hardening, packaging, and recommendation evaluation.

## Review rule

The active quota-aware review policy is in
`docs/workflow/model-effort-guide.md`.

- NORMAL tickets: Terra High implementation + independent Terra review
  subagent/check + required tests/CI is sufficient by default.
- SOL-GATED tickets: the implementation PR must remain **Draft** until a fresh
  separate Sol XHigh review returns APPROVE and validation is green.
- R3 risk classification alone does not create a Sol review gate.

Do not rely on old ticket wording that equates every R3 ticket with mandatory
flagship XHigh review; the central model-effort guide supersedes that review-model
selection.

## State-maintenance rule

`CURRENT.md` is maintained by implementation/delivery agents as part of ticket
handoff. The owner should not need to edit this file manually during normal
execution. Before delivery, the agent must make the current ticket state and
next valid ticket/handoff accurate.

## GitHub delivery state

GitHub Milestone objects for M12-M22 exist. Implementation issues and their PRs
should use the matching milestone. Every implementation PR must include
`Closes #<issue-number>` so merging to the default branch closes the ticket
automatically. The implementation agent should set the PR milestone through the
available GitHub tooling when possible and report explicitly if it cannot.

SOL-GATED PRs use Draft state as the merge blocker. NORMAL PRs should not carry
legacy blanket R3/XHigh blocker text.
