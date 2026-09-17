# Milestone and Ticket Index

Milestones M0-M11 are retained as project history. The active personal-assistant pivot continues the existing sequence at M12.

A ticket file is the implementation contract. One implementation ticket should normally be executed in one fresh coding-agent session. Review effort/path is defined by `docs/workflow/model-effort-guide.md`. See `CURRENT.md`, `AGENTS.md`, and `docs/workflow/ai-development-workflow.md`.

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

Historical ticket files remain useful evidence but are not active backlog contracts unless a current ticket explicitly references them.

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
| M19 | Core Recommendation Foundation | [TKT-M19-01](M19/tickets/TKT-M19-01.md), [TKT-M19-02](M19/tickets/TKT-M19-02.md), [TKT-M19-03](M19/tickets/TKT-M19-03.md), [TKT-M19-04](M19/tickets/TKT-M19-04.md) |
| M20 | Personal Learning and Existing Investment Tracking | [TKT-M20-01](M20/tickets/TKT-M20-01.md), [TKT-M20-02](M20/tickets/TKT-M20-02.md), [TKT-M20-03](M20/tickets/TKT-M20-03.md) |
| M21 | Signals and Crafting Intelligence | [TKT-M21-01](M21/tickets/TKT-M21-01.md), [TKT-M21-S01](M21/tickets/TKT-M21-S01.md), [TKT-M21-S02](M21/tickets/TKT-M21-S02.md), [TKT-M21-S03](M21/tickets/TKT-M21-S03.md), [TKT-M21-S04](M21/tickets/TKT-M21-S04.md), [TKT-M21-S05](M21/tickets/TKT-M21-S05.md), [TKT-M21-02](M21/tickets/TKT-M21-02.md), [TKT-M21-03](M21/tickets/TKT-M21-03.md) |
| M22 | Continuous Operation, Hardening, and Evaluation | [TKT-M22-01](M22/tickets/TKT-M22-01.md), [TKT-M22-02](M22/tickets/TKT-M22-02.md), [TKT-M22-03](M22/tickets/TKT-M22-03.md) |

The displayed UI labels for the primary surfaces remain French (`Mes Signaux`, `Artisanat`, `Réglages`); milestone/ticket/domain terminology remains English.

## Current explicit execution order

M12-M20 and TKT-M21-01 are complete. The owner-approved sequence from the merged #92 baseline is:

```text
#129 TKT-M21-S01  self-healing CURRENT live state
 -> #130 TKT-M21-S02  Signals UX spike (displayed as Mes Signaux)
 -> #131 TKT-M21-S03  Signals MVP + primary navigation
 -> #132 TKT-M21-S04  post-MVP cleanup
 -> #133 TKT-M21-S05  plan/Active-Passive/shadow/reconciliation engine
 -> #93  TKT-M21-02   crafting economic truth
 -> #94  TKT-M21-03   guided crafting paths + crafting UI (Artisanat)
 -> #95  TKT-M22-01   continuous decision loop + actionable notifications
 -> #96  TKT-M22-02   hardening/packaging
 -> #97  TKT-M22-03   Signal-plan outcome evaluation
```

Do not infer the next ticket from GitHub issue number ordering. `CURRENT.md` plus this sequence and live GitHub state define the current handoff.

## Product checkpoints

- after TKT-M21-S03 / #131: first usable attention-first French Signals UI displayed as `Mes Signaux`;
- after TKT-M21-S05 / #133: stable executable Active/Passive plans with responsive reversible local shadow/reconciliation;
- after TKT-M21-03 / #94: crafting is a first-class guided profit engine with the workspace displayed as `Artisanat`;
- after TKT-M22-01 / #95: permitted state changes can surface materially new Signals without repeated manual checking;
- after TKT-M22-02 and TKT-M22-03: the 0.1 shape is hardened and can evaluate observed outcomes without fabricated counterfactuals.

Investment-position/staged-exit infrastructure delivered in M20 remains valid, but new investment-discovery/seasonality work is deferred beyond the 0.1 focus.

## Dependency spine

```text
M12-M18 local/account/history foundation
 -> M19 deterministic recommendation foundation
 -> M20 personal evidence
 -> M21-01 crafting-account evidence
 -> Signals transition (S01-S05)
 -> crafting economics/path UI (M21-02/03)
 -> continuous loop/hardening/evaluation (M22)
```

After TKT-M18-02, keep the local collector running during subsequent development whenever practical so owned history continues to accumulate.

## GitHub live-state authority

GitHub merged PR/closed issue/milestone state is authoritative for live delivery state. TKT-M21-S01 / #129 makes the bounded generated live-state block in `CURRENT.md` self-healing after merges, with session-start reconciliation as fallback. Historical Markdown tickets remain durable contracts/evidence; live status must not be inferred from stale prose when GitHub disagrees.

## GitHub Milestone objects

Existing GitHub milestone objects M12-M22 are retained. The M21 object is reused for the Signals transition plus remaining crafting work so renumbering is unnecessary. Repository docs define the explicit execution order above.