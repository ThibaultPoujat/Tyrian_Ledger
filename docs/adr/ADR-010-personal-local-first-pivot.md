# ADR-010 - Personal Local-First Trading Assistant Pivot

## Status

Accepted by the owner on 2026-09-04 for the M12 pivot.

## Context

M10-M11 transformed Tyrian Ledger into a public static beginner fast-flip site
using a periodically generated public market snapshot. That architecture was
secure and internally coherent, but it removed the account awareness, durable
personal history, historical market research, and local persistence required by
the owner's actual product goal.

The repository also contains mature reusable work that should not be discarded:
integer-copper finance, fee primitives, order-book simulation, typed ArenaNet
HTTP infrastructure, request scheduling/batching/error handling, React UI work,
fixtures, and broad tests.

## Decision

Tyrian Ledger will pivot into a **local-first personal Guild Wars 2 Trading Post
assistant** and preserve the proven existing engineering foundation rather than
restart in a different stack.

The target runtime is:

`React UI -> loopback ASP.NET Core host/API -> Application/Analytics -> SQLite + typed read-only ArenaNet gateway`

The application may use a dedicated ArenaNet API key for verified read-only
personal endpoints. Credentials remain outside browser/source/database and use
the secret boundary in ADR-006.

The product will progressively add:

- personal current/completed Trading Post synchronization;
- durable local accounting and FIFO cost basis;
- current market scanner with exact fees/order-book depth;
- owned historical market collection/statistics;
- explainable opportunity scoring and bankroll-aware sizing;
- a primary deterministic `What Should I Do?` action screen;
- personal fill/capital-turnover learning;
- investment tracking;
- later crafting economics and bounded path analysis;
- local alerts and outcome evaluation.

No feature may automate gameplay or execute/cancel/update Trading Post orders.

## Supersession

This ADR supersedes ADR-008 and ADR-009 for active product/runtime direction.
Those ADRs remain historical records explaining why static Pages and the
external scheduler exist at the start of M12.

ADR-001's core stack, ADR-002 local-first deployment (clarified platform-
neutrally), ADR-004 typed gateway, ADR-005 integer-copper money, ADR-006 secret
storage, and ADR-007 read-only boundary remain active unless separately amended.

## Financial authority

Authoritative financial and recommendation logic remains deterministic C# code
with tests. React consumes structured results and must not carry a second
competing implementation of fees, accounting, or recommendation formulas.

Historical and personal statistics expose sample count/coverage and are not
prediction guarantees. Missing cost basis and insufficient evidence stay
explicit.

## Consequences

- M12 first updates the source of truth, then retires obsolete static delivery,
  then establishes a clean post-pivot baseline.
- The repository is evolved incrementally; no broad rewrite is authorized.
- SQLite becomes durable user-owned data and therefore backup/migration safety
  is a first-class requirement.
- Security review expands from public-site secrecy to private account-data and
  local-host boundaries.
- Independent review is mandatory for each PR and especially strong for
  financial/accounting/persistence/security/statistical/recommendation changes.
