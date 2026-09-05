# Milestone and Ticket Index

Milestones M0-M11 are retained as project history. The active personal
trading-assistant pivot continues the existing sequence at **M12**.

A ticket file is the implementation contract. One implementation ticket should
normally be executed in one fresh coding-agent session, followed by a fresh
independent review session. See `CURRENT.md`, `AGENTS.md`, and
`docs/workflow/ai-development-workflow.md`.

## Historical milestones

| Milestone | Name |
|---|---|
| M0 | Discovery and external-contract validation |
| M1 | Repository and development foundation |
| M2 | GW2 data gateway and caching |
| M3 | Deterministic market engine |
| M4 | Dashboard and session planning |
| M5 | Account-aware analysis and crafting (historical plan) |
| M6 | Personal history and reconciliation (historical plan) |
| M7 | Historical market data and investment research (historical plan) |
| M8 | Hardening, accessibility, release readiness |
| M9 | Beginner fast-flip MVP |
| M10 | Static GitHub Pages snapshot deployment |
| M11 | Published snapshot reliability |

Historical ticket files remain useful evidence but are not active backlog
contracts unless a current ticket explicitly references them.

## Active pivot roadmap

| Milestone | Name | Tickets |
|---|---|---|
| M12 | Controlled Personal-Assistant Pivot | [TKT-M12-01](M12/tickets/TKT-M12-01.md), [TKT-M12-02](M12/tickets/TKT-M12-02.md), [TKT-M12-03](M12/tickets/TKT-M12-03.md) |
| M13 | Local Runtime and Authenticated Read-Only Gateway | [TKT-M13-01](M13/tickets/TKT-M13-01.md), [TKT-M13-02](M13/tickets/TKT-M13-02.md), [TKT-M13-03](M13/tickets/TKT-M13-03.md) |
| M14 | Durable Personal Data | [TKT-M14-01](M14/tickets/TKT-M14-01.md), [TKT-M14-02](M14/tickets/TKT-M14-02.md), [TKT-M14-03](M14/tickets/TKT-M14-03.md) |
| M15 | Trustworthy Accounting | [TKT-M15-01](M15/tickets/TKT-M15-01.md), [TKT-M15-02](M15/tickets/TKT-M15-02.md), [TKT-M15-03](M15/tickets/TKT-M15-03.md) |
| M16 | Personal Dashboard and Current Orders | [TKT-M16-01](M16/tickets/TKT-M16-01.md) |
| M17 | Live Market Intelligence | [TKT-M17-01](M17/tickets/TKT-M17-01.md), [TKT-M17-02](M17/tickets/TKT-M17-02.md), [TKT-M17-03](M17/tickets/TKT-M17-03.md) |
| M18 | Owned Historical Market Dataset | [TKT-M18-01](M18/tickets/TKT-M18-01.md), [TKT-M18-02](M18/tickets/TKT-M18-02.md), [TKT-M18-03](M18/tickets/TKT-M18-03.md) |
| M19 | Core Recommendation Product | [TKT-M19-01](M19/tickets/TKT-M19-01.md), [TKT-M19-02](M19/tickets/TKT-M19-02.md), [TKT-M19-03](M19/tickets/TKT-M19-03.md), [TKT-M19-04](M19/tickets/TKT-M19-04.md) |
| M20 | Personal Learning and Investments | [TKT-M20-01](M20/tickets/TKT-M20-01.md), [TKT-M20-02](M20/tickets/TKT-M20-02.md), [TKT-M20-03](M20/tickets/TKT-M20-03.md) |
| M21 | Crafting Intelligence | [TKT-M21-01](M21/tickets/TKT-M21-01.md), [TKT-M21-02](M21/tickets/TKT-M21-02.md), [TKT-M21-03](M21/tickets/TKT-M21-03.md) |
| M22 | Convenience, Hardening, and Evaluation | [TKT-M22-01](M22/tickets/TKT-M22-01.md), [TKT-M22-02](M22/tickets/TKT-M22-02.md), [TKT-M22-03](M22/tickets/TKT-M22-03.md) |

## Dependency spine

```text
M12 pivot
 -> M13 local host/account gateway
 -> M14 durable personal data
 -> M15 accounting
 -> M16 personal dashboard
 -> M17 live scanner
 -> M18 owned market history
 -> M19 recommendation product
 -> M20 personal learning/investments
 -> M21 crafting
 -> M22 hardening/evaluation
```

Some later tickets have narrower dependencies and may technically start earlier,
but the default owner workflow follows this sequence to minimize simultaneous
architectural risk. The backend-persisted M17 watchlist depends on the M14
persistence foundation. M22 whole-product hardening follows all user-facing
feature branches and gates recommendation-outcome evaluation. After TKT-M18-02
lands, keep the local collector running while subsequent milestones are
developed so history accumulates.

## GitHub Milestone objects

The Markdown sequence and ticket IDs are authoritative. GitHub Milestone objects
may be created manually with matching names `M12` through `M22` and assigned to
the corresponding issues. Their absence does not change ticket dependencies.
