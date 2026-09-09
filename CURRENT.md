# Current Project State

Last updated: 2026-09-09

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

**M19 - Core Recommendation Product**

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

TKT-M16-01 / #78 merged in PR #112. It adds a
backend-authoritative local dashboard and current-order view: manual sync,
connection and retained-coverage status, 7/30/90 realized results, separately
labeled open/unrealized exposure, current buy/sell capital, recent trades,
known-basis best/worst items, and top-of-book comparisons. The browser receives
only safe structured result data; fee, P&L, liquidation, and market comparison
logic stay in the application/backend layers. Missing coverage, unknown basis,
partial liquidation depth, and unavailable market evidence remain explicit.

TKT-M17-01 / #79 merged in PR #113. It adds a backend-authoritative
current aggregate-market scanner with configurable ROI/profit and bid/list
policy, exact canonical fee economics, maximum integer bid, aggregate side
quantity, observation time, and structured inclusion/exclusion evidence. The
local no-store API encodes copper as strings and reports the fee-rounding model
as provisional while VERIFY-013 remains OPEN. It deliberately does not read
detailed order books, size positions, persist history, or add scanner UI.

TKT-M17-02 / #80 merged in PR #114. It enriches every scanner shortlist
candidate with complete visible order-book depth, near-best quantity/listing
evidence, exact simulator acquisition/liquidation outcomes for a requested
quantity, next-level price gaps/cliffs, shallow-book reasons, and a conservative
visible-depth participation cap. The evidence remains current-book-only and
does not become historical volume, a fill guarantee, or final portfolio sizing.

TKT-M17-03 / #81 merged in PR #115. It adds an accessible local scanner screen
with backend-returned current economics, filters/sorts, freshness and
risk/rejection visibility, bounded same-scan order-book detail, and a durable
local SQLite watchlist that works without account connection. React only
displays backend financial and liquidity evidence.

TKT-M18-01 / #82 merged in PR #116. It adds
versioned SQLite storage for immutable best-price observations and deliberate
optional full-book captures, with UTC/integer-copper invariants, strict backup
validation, item/time indexes, and an append-only repository boundary. Its
typed adaptive policy composes current personal orders, watchlist entries, and
future registered sources; full books require explicit high-interest opt-in.

TKT-M18-02 / #83 merged in PR #117. It starts a loopback hosted collector that resumes per-item cadence
from retained aggregate observations, uses the typed gateway's existing request
budget/batching/retry behavior, appends only valid complete evidence, exposes
safe no-store health, and accepts a protected manual one-shot run. The worker
rechecks typed source membership every configurable minute by default; detailed
books remain an explicit policy opt-in. VERIFY-004, VERIFY-005, VERIFY-006,
VERIFY-010, and VERIFY-011 remain OPEN with conservative configurable limits;
no live keyed probe was performed.

TKT-M18-03 / #84 merged in PR #118. It adds a read-only local history-status
API with measured database size, per-item/time-window aggregate and
detailed-book coverage, and on-demand integrity results. Retention policy
version 1 independently preserves all raw aggregate and detailed-book evidence:
it performs no deletion, rewriting, or downsampling without a future
owner-approved migration. Populated backup/restore and clear-personal-data tests
prove market history remains recoverable and separate from account-scoped
clearing. PR #120 follows up the #118 NORMAL review with a managed-backup
restore path so retained history is not bounded by the 512 MiB imported-file
upload cap, and maps out-of-range SQLite migration IDs to failed integrity
rather than a server error. No VERIFY entries changed.

TKT-M19-01 / #85 merged in PR #119. It adds deterministic historical market
baselines from retained aggregate observations, with latest-observed modeled
ROI and coverage-aware 7/30-day persistence, volatility, depth, range, and
drawdown evidence. Insufficient samples or observed span remain explicit, and
the analytics do not predict prices, fills, or profit.

TKT-M19-02 / #86 merged in PR #121. The versioned deterministic score
combines bounded current economics, visible liquidity, historical persistence,
stability, and explicit 7/30-day confidence, then exposes every named component
and anomaly penalty. Missing history contributes no invented stability;
extreme ROI, shallow books, price cliffs, abrupt price/depth changes, and an
intended quantity above visible-depth participation remain structured flags.
Personal evidence is an explicit zero-weight placeholder for later sufficiently
sampled M20 work. No scanner/API/UI orchestration or final position sizing is
included.

TKT-M19-03 / #87 is implemented in Draft PR #122. It adds a pure deterministic
position-sizing policy with a 15% reserve; 5%/3%/1.5% high/medium/low-liquidity
item caps; and 20% strategy/25% category caps. Explicit complete portfolio
snapshots include current-order/position capital at risk; unknown, negative,
duplicate, or incomplete state returns no allocation. Ranked candidates consume
cash and grouped capacity sequentially, and every binding cap remains structured
for later M19-04 orchestration. The Draft remains SOL-GATED pending the
owner-triggered fresh Sol XHigh review; no VERIFY entries changed.

After the #87 SOL-GATED review/merge handoff, the next valid implementation
ticket is:

**TKT-M19-04 / #88 - Build the Primary `What Should I Do?` Recommendation
Screen.**

## Known-good baseline

TKT-M19-02 local validation on 2026-09-09 reported:

- Release solution build: zero warnings and zero errors;
- focused opportunity scoring: 14 tests passed; combined scoring, historical,
  scanner, order-book, and fee prerequisite suites: 25 Application and 20
  Analytics tests passed;
- full .NET regression suite: 362 tests passed (4 Domain, 21 Analytics, 137
  Application, 160 Infrastructure, and 40 Web);
- React: 21 component tests passed and the production build succeeded;
- Playwright: 12 tests passed across Chromium, Firefox, and WebKit;
- CI workflow contracts: 3 tests passed;
- retired-runtime audit: only expected negative assertions matched;
- Gitleaks: complete reachable history scanned with no leaks.

PR URL: https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/122 (Draft; pending targeted fresh Sol XHigh re-review)

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
