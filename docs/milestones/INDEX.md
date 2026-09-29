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

| Ticket | Purpose | Readiness |
|---|---|---|
| [TKT-M22-P00](M22/tickets/TKT-M22-P00.md) / #148 | Approved product, six visual references and workflow preparation | Merged in #151; target baseline recorded |
| [TKT-M22-P01A](M22/tickets/TKT-M22-P01A.md) / #149 | Aggregate resource demands in selection and atomic start | Merged in #152; F01 resource-admission fix complete |
| [TKT-M22-C01](M22/tickets/TKT-M22-C01.md) / #153 | P01A checkpoint and next ticket preparation | Planning/docs preparation; no application change |
| [TKT-M22-P01B](M22/tickets/TKT-M22-P01B.md) / #154 | Step-bound durable completion commands | Ready after #153 merges |
| [TKT-M22-G01](M22/tickets/TKT-M22-G01.md) / #150 | P01–P06 integration/checkpoint evidence | Tracking gate only; not an executable coding Goal |

Target navigation is `Signaux / Plans / Bilan`, with `Réglages` at the bottom.
See [approved packages and exit criteria](approved-delivery-plan.md).

## Current explicit execution order

The historical predecessors remain listed for deterministic handoff generation.
They are closed; their merged features do not imply the audit findings are fixed.
The owner-approved corrective sequence now precedes release hardening:

```text
#128 -> #129 -> #130 -> #131 -> #132 -> #133 -> #93 -> #94 -> #95 -> #145
 -> #148 TKT-M22-P00   approved product and visual baseline
 -> #149 TKT-M22-P01A  duplicate resource demand correctness
 -> #153 TKT-M22-C01   P01A checkpoint and P01B preparation
 -> #154 TKT-M22-P01B  step-bound durable completion commands
 -> #150 TKT-M22-G01   tracking gate for remaining P01–P06 evidence
 -> #96  TKT-M22-02    release hardening after all prerequisite packages
 -> #97  TKT-M22-03    outcome evaluation acceptance
```

No active non-blocking alternate is authorized. Do not start the tracking gate as
one coding task. After #154, create the next bounded Ready child ticket and insert
it before #150 in both this file and issue #98. Keep #150 open until all package
exit evidence exists and the owner accepts progression. Do not skip it because
there are no more ready coding tickets. New dependencies/review gates must be
explicitly recorded before implementation.

## Historical non-blocking rule

Retained for the existing deterministic updater and historical regression cases;
all of these predecessor issues are closed. This grants no new alternate route:

**MVP non-blocking exception:** #129 is preferred before #130. After #128 merges,
#130 may start before #129. #131 depends on #130, not on completion of #129.

The [P01A checkpoint](M22/checkpoint-p01a.md) records accepted #152 evidence and
why command replay safety is next. Completing #154 will still not close P01.

## Authority and checkpoints

GitHub owns merged/open/closed/milestone state. Issue #98 and this file own order;
`docs/workflow/model-effort-guide.md` owns effort and active Sol gates.
`CURRENT.md` is a derived handoff, not permission to start a non-Ready gate.
Source contradictions must be repaired, not resolved by numeric issue order.

P00–P07 in [the delivery plan](approved-delivery-plan.md) map all 19 audit findings.
P01/P02 share an integration gate. P05 requires the latest six reference images,
actual application screenshots and owner usability review. P06 requires Windows
notification/lifecycle evidence with the UI closed. P07 requires release evidence.
Existing M12–M22 GitHub milestone objects are retained; corrective tickets belong
to M22. Optional external-history research #139 does not block resource repairs.
