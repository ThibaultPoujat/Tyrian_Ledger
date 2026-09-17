# Permanent Context

## Identity

Tyrian Ledger is a **local-first second-screen personal Guild Wars 2 profit
assistant**. It turns market/account evidence into a small number of concrete
manual actions while the owner continues to play the game.

The primary daily Signals surface is displayed as **`Mes Signaux`**. The
application is not a trading bot, gameplay bot, order executor, browser
automator, or autonomous game agent. Codex/coding agents are development tools
only; no application LLM participates in runtime financial truth.

## 0.1 product focus

0.1 focuses on:

1. Trading Post flipping/trading;
2. crafting-for-profit;
3. the shared Signal/Plan/Steps/Reconciliation/Outcome execution model;
4. reliable local operation, packaging and observed outcome evaluation.

Existing investment-position/staged-exit infrastructure remains valid and must
not be deleted merely because it is not the current product focus. Investment
opportunity discovery/seasonality is deferred from the 0.1 critical path.

Target displayed primary navigation:

`Mes Signaux / Artisanat / Réglages`

Dashboard, scanner, raw inventory, personal learning, order books and retained
history are supporting engines/evidence rather than primary product
responsibilities.

## Repository and displayed language

Repository-facing artifacts use **English**. This includes documentation
filenames and ordinary documentation prose, code/type/API/database/migration
identifiers, internal domain terminology, reason codes, test identifiers and
configuration keys. Exact French strings may appear in docs only when quoting or
specifying displayed product copy.

All user-facing UI/UX labels and text are written in **French**: navigation,
actions, buttons, headings, helper text, errors, empty/degraded states,
notifications, explanations and accessibility text.

Proper nouns and technical identifiers may stay canonical when translating them
would reduce clarity. Financial logic must never depend on localized strings.

## Signal definition

A Signal is an opportunity sufficiently safe, profitable, relevant and
compatible with the owner's current state to justify a concrete manual action.

Internal analysis does not automatically deserve attention. `WAIT`, `HOLD`,
`KEEP BID`, `REVIEW`, harmless undercut/outbid and similar no-action states
normally remain silent on the displayed `Mes Signaux` surface unless they imply
a concrete corrective action.

Canonical internal product flow:

`Intelligence -> Opportunity -> Plan -> Steps -> Signal -> Reconciliation -> Outcome`

See `docs/specs/signals.md` for the full product/interaction contract.

## Pre-0.1 development status

Until the owner explicitly declares the first `0.1` release, Tyrian Ledger is a
pre-release development project with **no production users and no user data that
must be preserved across development changes**. Manual owner testing does not
create a backward-compatibility or data-retention obligation.

During this pre-0.1 period:

- development/test databases and locally generated test data may be treated as
  disposable;
- do not add compatibility migrations, data-conversion paths, legacy-schema
  support, or one-off preservation procedures solely to protect pre-0.1 local
  data unless the assigned ticket explicitly requires them;
- prefer the cleanest correct target schema/behavior when a pre-release change
  would otherwise require carrying legacy development state;
- documented development setup may require clearing/recreating local data when
  necessary, provided secrets remain protected and the action is explicit;
- do not infer deployed-user constraints, upgrade guarantees, or production
  migration obligations before `0.1`.

This does **not** authorize removing or weakening migration, backup/restore,
recovery, integrity, or data-safety capabilities that are part of the intended
0.1 product.

## Hard constraints

- Read-only toward Guild Wars 2; no gameplay or Trading Post automation.
- A dedicated ArenaNet API key may be used only by local host/infrastructure for
  verified read-only endpoints.
- No secret value in source, browser code/storage, frontend payloads, logs,
  fixtures, prompts, tests, commits, PRs, or SQLite.
- Browser never accesses the OS secret store or ArenaNet directly.
- Normal host binds only to loopback, validates allowed Host values, serves
  production UI/API same-origin, allowlists exact development origins, and
  separately protects state-changing endpoints from cross-origin requests.
- All ArenaNet access goes through typed gateway abstractions with request
  policy/rate limiting.
- External DTOs stay separate from domain/application models.
- Authoritative money is integer copper.
- Financial/accounting/recommendation/plan logic is deterministic and tested.
- Unknown cost basis/insufficient history remains explicit.
- Never invent ArenaNet fields, permissions, quotas, endpoints, timestamp
  semantics, cache behavior, fee behavior, or limits; use VERIFY.
- No runtime LLM or opaque ML financial model.
- No recommendation score or plan utility may override a failed hard
  safety/evidence/liquidity/risk requirement.

## Target stack

- .NET 10 / ASP.NET Core loopback local host;
- React + TypeScript frontend;
- SQLite local persistence;
- existing Domain/Application/Analytics/Infrastructure libraries;
- xUnit and frontend tests;
- Playwright E2E.

## Target architecture

`React -> local ASP.NET Core API -> Application/Analytics -> SQLite + typed ArenaNet gateway`

The M10-M11 public static Pages architecture is historical and superseded. Do
not restore it as a product runtime.

## Product priorities from current baseline

The accounting/scanner/history/recommendation/personal-learning foundations are
already built. Current priority order is:

1. make live project handoff state self-healing from GitHub;
2. validate and ship the first usable French Signals UI (`Mes Signaux`) MVP quickly;
3. remove superseded UI/docs/code after replacement paths are proven;
4. add deterministic plan/bundle selection, Passive/Active paths, resource
   reservations, reversible local execution shadow state, Undo and ArenaNet
   reconciliation;
5. complete crafting economic truth and guided bounded crafting plans;
6. add the continuous decision loop/actionable local notifications;
7. harden/pack/evaluate observed plan outcomes.

The explicit live sequence is maintained in issue #98, `docs/milestones/INDEX.md`
and the generated live block in `CURRENT.md`.

## Effective planning state

The long-term execution model uses:

`effective planning state = latest verified ArenaNet state + locally recorded unconfirmed execution events`

Local execution shadow state is reversible/provisional and stored separately
from verified state. It exists to keep manual Active paths responsive while the
API catches up. Verified ArenaNet evidence eventually wins; material
contradiction pauses for explicit reconciliation rather than guessing.

## Performance semantics

Main assistant headline performance is 30-day realized profit, with 7-day and
90-day realized results as secondary context. Open/unrealized result is separate.

Internal realized strategy categories must be additive/non-overlapping for the
same supported population/window:

- `Trading/Flipping`;
- `Crafting`;
- `Unclassified`.

The UI renders French labels for those categories. Ambiguous strategy remains
`Unclassified` until later deterministic evidence can resolve it. Crafting may
additionally show value added versus the best realistic input alternative, but
that value is never added again to global realized profit.

## `CURRENT.md` live-state authority

GitHub merged PRs, closed issues and milestone/roadmap state are authoritative
for **live delivery state**. `CURRENT.md` should contain durable context plus a
small clearly-delimited generated live-state block.

TKT-M21-S01 / #129 adds deterministic post-merge automation with no AI quota.
Before that ticket lands, and as a fallback afterward, a coding agent must
compare the generated block with GitHub at session start and repair a stale live
block before relying on it. The owner should not manually maintain normal
handoff state.

## Required session context

1. reconcile/read `CURRENT.md` live state against GitHub;
2. `AGENTS.md`;
3. this file;
4. current milestone context;
5. assigned ticket;
6. relevant `docs/verification/VERIFY-REGISTER.md` entries;
7. `docs/workflow/model-effort-guide.md`;
8. `docs/specs/signals.md` for Signal/plan/UI work;
9. only additional specialized source/spec/ADR files needed by the ticket.

Do not load all historical milestone/ticket documents.

## Session and review rule

One implementation ticket normally uses one implementation session. Work in one
isolated branch/worktree, make a short in-session plan, implement, validate,
inspect the diff, run the required review path, open/update the PR, provide a
functional summary, and stop.

Dedicated Plan mode is not mandatory for every ticket; use
`docs/workflow/model-effort-guide.md`. If a genuine ambiguity, contradiction or
owner decision cannot be resolved from repository context, pause and ask the
owner with a recommended choice and concise alternatives. Routine technical
choices remain autonomous.

Review follows the model-effort guide:

- NORMAL tickets use an independent Terra review subagent/check in the same run
  when supported;
- SOL-GATED tickets stay Draft, complete required validation/CI before fresh
  separate Sol XHigh review, and use targeted fresh Sol re-review after scoped
  fixes;
- quota pressure changes scheduling, not correctness gates.

`.codex/skills/tyrian-pr-review/SKILL.md` defines the independent findings-first
review checklist. Git/tests/tickets/docs are the durable handoff; chat memory is
not.

## VERIFY versus BLOCKED

`VERIFY` means an external fact is unresolved but safe work can continue without
assuming it. `BLOCKED` means missing/contradictory information makes the work
unsafe or impossible without evidence or an owner decision.

Record material uncertainty once with enough context; do not repeatedly
research the same unresolved fact without new evidence.

## Owner decision gates

The owner controls product scope, architecture/ADR changes, API permission/key
requirements, canonical financial policy, destructive persistence/retention,
network exposure, paid/production dependencies, release/merge, and any proposal
to cross the read-only automation boundary.