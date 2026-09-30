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
| M22 | Continuous Operation, Hardening, and Evaluation | [TKT-M22-01](M22/tickets/TKT-M22-01.md), [TKT-M22-S01](M22/tickets/TKT-M22-S01.md), [TKT-M22-02](M22/tickets/TKT-M22-02.md), [TKT-M22-03](M22/tickets/TKT-M22-03.md) |

## Corrective contracts

Completed foundations: P00 / #148 in #151, P01A / #149 in #152,
C01 / #153 in #155, P01B / #154 in #156. Accepted evidence is linked in
[batch B1](M22/batch-01.md); old feature merges do not close new audit findings.

| Ticket | Purpose | Conditional readiness |
|---|---|---|
| [C02 / #157](M22/tickets/TKT-M22-C02.md) | Remaining delivery map and four-ticket preparation | Current planning/workflow preparation |
| [P01C / #158](M22/tickets/TKT-M22-P01C.md) | Source-scoped reconciliation evidence | After #157 merges |
| [P01D / #159](M22/tickets/TKT-M22-P01D.md) | Safe partial completion and bounded residual instructions | After #158 merges with required evidence |
| [P05A / #160](M22/tickets/TKT-M22-P05A.md) | Approved Signaux, shell and session preview | After #159 merges |
| [P05B / #161](M22/tickets/TKT-M22-P05B.md) | Approved comparison and execution preview | After #160 merges |
| [G01 / #150](M22/tickets/TKT-M22-G01.md) | Batch / P01–P06 integration checkpoint | Tracking gate only, not a coding Goal |

See the [remaining dependency map](remaining-delivery-map.md),
[approved package exits](approved-delivery-plan.md) and
[reusable Goal](../workflow/goal-session.md). Only B1's four implementation
contracts are prepared. Future map units are Planned, not Ready.

## Current explicit execution order

Historical predecessors remain listed for deterministic handoff generation.
The approved batch precedes the integration gate and release hardening:

```text
#128 -> #129 -> #130 -> #131 -> #132 -> #133 -> #93 -> #94 -> #95 -> #145
 -> #148 TKT-M22-P00   approved product and visual baseline
 -> #149 TKT-M22-P01A  duplicate resource demand correctness
 -> #153 TKT-M22-C01   P01A checkpoint and P01B preparation
 -> #154 TKT-M22-P01B  step-bound durable completion commands
 -> #157 TKT-M22-C02   rolling delivery map and B1 preparation
 -> #158 TKT-M22-P01C  source-scoped reconciliation evidence
 -> #159 TKT-M22-P01D  partial completion and safe residual instructions
 -> #160 TKT-M22-P05A  approved Signaux and session preview
 -> #161 TKT-M22-P05B  approved comparison and execution preview
 -> #150 TKT-M22-G01   batch checkpoint; remaining P01–P06 exit evidence
 -> #96  TKT-M22-02    release hardening after package acceptance
 -> #97  TKT-M22-03    outcome evaluation acceptance
```

No active non-blocking alternate is authorized. Each new Goal checks actual
predecessor merges/review and its entry conditions; an open or preferred ticket
is not automatically Ready. After each B1 merge, use the next prepared contract
without an Astra planning session. After #161, stop coding at #150: assess B1
and prepare the next 3–5 contracts in both order authorities. Keep #150 open
until P01–P06 exits and owner progression approval. No automatic next-ticket
execution, auto-merge or skip to #96/#97.

## Historical non-blocking rule

Retained for the deterministic updater and historical regressions; these issues
are closed and grant no active alternate route:

**MVP non-blocking exception:** #129 is preferred before #130. After #128 merges,
#130 may start before #129. #131 depends on #130, not on completion of #129.

## Authority and checkpoints

GitHub owns delivery state; #98 and this file own order; the model guide owns
review assignment. CURRENT is derived and is not permission to start an unready
contract. Source disagreement fails closed. Corrective work stays in M22; future
map identifiers are not issue numbers. #139 external-history research remains
optional. P01/P02 close together; fixture UI is not live functionality; original
six PNGs and actual screenshots remain mandatory. Owner merges/releases.
