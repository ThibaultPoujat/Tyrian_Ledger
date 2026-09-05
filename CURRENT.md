# Current Project State

Last updated: 2026-09-05

## Active direction

Tyrian Ledger is pivoting from the M10-M11 public static Pages product into a
**local-first personal Guild Wars 2 Trading Post assistant**.

The owner has approved the product vision and the architectural direction:

`React UI -> loopback ASP.NET Core host/API -> deterministic application logic -> SQLite + typed read-only ArenaNet API gateway`

The existing C# financial/domain/API foundation and useful React/test work are
to be preserved. Static Pages publishing, the external Pages scheduler, the
public market-snapshot runtime, and browser-side duplicate authoritative
recommendation calculations are transition code to retire in M12 rather than
new architecture to extend.

## Active milestone

**M12 - Controlled Personal-Assistant Pivot**

TKT-M12-02 has removed the active static Pages runtime on its implementation
branch. Its pull request awaits the required fresh independent review and owner
merge decision.

After TKT-M12-02 is merged, the next implementation ticket is:

**TKT-M12-03 - Establish the post-pivot quality baseline.**

Do not begin TKT-M12-03 on this branch or before the review/merge handoff is
complete.

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

Every implementation PR receives a fresh independent review. Use
`.codex/skills/tyrian-pr-review/SKILL.md`. R3 financial, security, private-data,
network-exposure, and architecture-authority changes require a fresh
flagship-model XHigh review.

## Owner actions outside the repository

GitHub Milestone objects for M12-M22 may be created manually and assigned to the
matching issues. The Markdown milestone/ticket files and issue prefixes remain
authoritative even if GitHub Milestone objects are absent.
