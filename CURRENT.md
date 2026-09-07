# Current Project State

Last updated: 2026-09-07

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

**M16 - Personal Dashboard and Current Orders**

M13 is complete through the local host, secure API-key validation, and typed
personal Trading Post gateway work merged in PRs #102-#105. M14 is complete
through versioned SQLite persistence, idempotent personal TP synchronization,
and local backup/restore/clear controls merged in PRs #106-#108.

TKT-M15-01 / #75 is merged in PR #109. It establishes one canonical
application-layer GW2 fee policy with separate 5% listing and 10% exchange fees,
a 1-copper positive-sale minimum for each, and owner-approved independent
round-up. ArenaNet support and the linked official wiki do not define
fractional-copper rounding, so VERIFY-013 remains OPEN and every fee-derived
result remains explicitly modeled/provisional.

TKT-M15-02 / #76 merged in PR #110. It reconstructs known acquisition inventory
from completed personal Trading Post transactions, allocates sells to the oldest
available buy lots by account and item, supports partial/many-to-one/one-to-many
matches, and keeps missing pre-history basis as an explicit unknown quantity.
Equal completed timestamps use ascending external transaction ID, and the
versioned result is rebuilt in memory without derived SQLite state or
current-order inference.

TKT-M15-03 / #77 merged in PR #111. It provides deterministic known-basis
realized P&L, explicit unknown-basis exclusion, open FIFO basis, and current
liquidation/unrealized results only where complete market evidence exists.
VERIFY-013 remains OPEN, so every fee-derived value is provisional.

TKT-M16-01 / #78 is implemented on `codex/TKT-M16-01-dashboard`. It adds a
backend-authoritative local dashboard and current-order view: manual sync,
connection and retained-coverage status, 7/30/90 realized results, separately
labeled open/unrealized exposure, current buy/sell capital, recent trades,
known-basis best/worst items, and top-of-book comparisons. The browser receives
only safe structured result data; fee, P&L, liquidation, and market comparison
logic stay in the application/backend layers. Missing coverage, unknown basis,
partial liquidation depth, and unavailable market evidence remain explicit.

TKT-M16-01 is **NORMAL**. Required local validation is green; independent
review remains for the owner to trigger. PR #112 is open for the owner review.
After the #78 merge handoff, the next valid implementation ticket is:

**TKT-M17-01 / #79 - Build the Live Fee-Aware Market Scanner.**

## Known-good baseline

TKT-M16-01 local validation on 2026-09-07 reported:

- Release solution build: zero warnings and zero errors;
- focused dashboard query/accounting: 80 tests passed; web integration: 30 tests passed;
- full .NET: 268 tests passed;
- React: 11 component tests passed and the production build succeeded;
- Playwright: 9 tests passed across Chromium, Firefox, and WebKit;
- CI workflow contracts: 3 tests passed;
- retired-runtime and competing-fee-formula audits: no unexpected matches;
- Gitleaks: complete reachable history scanned with no leaks.

PR #111 is merged into `develop` as merge commit
`c925d37ac201b55022b8b729d4c0d5e70c2902d5`.

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
