# Tyrian Ledger — Coding Agent Instructions

## Mission

Build Tyrian Ledger as a **local-first second-screen personal Guild Wars 2 profit
assistant**. The product continuously turns account/market evidence into the
smallest useful set of concrete manual actions for the owner to perform in game.

The primary daily surface is **`Mes Signaux`**. The 0.1 economic focus is Trading
Post flipping/trading plus crafting. Existing investment-position/staged-exit
infrastructure is preserved, but investment discovery/seasonality is deferred
from the 0.1 critical path.

The coding model is a development tool only. It is never part of runtime
financial truth and never executes gameplay or Trading Post actions.

M0-M11 are historical milestones. The active local-first pivot begins at M12.
Follow current source-of-truth documents rather than inferring product intent
from old files.

## Pre-0.1 development-data policy

Until the owner explicitly declares the first `0.1` release, this is a
**pre-release development project with no production users and no user data that
must be preserved merely for compatibility**. Manual owner testing before 0.1
does not create a migration/backward-compatibility obligation.

Follow `docs/context/permanent-context.md`:

- do not carry legacy development state or add preservation-only migrations
  unless the assigned ticket requires them;
- prefer the cleanest correct target schema/behavior;
- it is acceptable for development setup to require clearing/recreating local
  data when explicitly documented and secrets remain protected;
- do not weaken migration, integrity, backup/recovery or data-safety capabilities
  that are part of the intended released product.

## Live handoff authority and `CURRENT.md`

GitHub merged PRs, closed issues and active roadmap/milestone state are
authoritative for **live delivery state**.

`CURRENT.md` contains durable context plus a clearly delimited generated
live-state block. TKT-M21-S01 / #129 adds deterministic post-merge maintenance
without AI/model quota.

At the **start of every implementation session**:

1. inspect/reconcile the live GitHub issue/PR state relevant to the roadmap;
2. compare it with the generated live block in `CURRENT.md`;
3. if they disagree, treat GitHub as authoritative and repair the stale live
   block before relying on it;
4. do not ask the owner to maintain `CURRENT.md` manually during normal work.

Never rewrite durable `CURRENT.md` narrative merely to update live ticket state.

## Read order for an implementation ticket

1. Reconcile/read `CURRENT.md` against live GitHub state.
2. This file.
3. `docs/context/permanent-context.md`.
4. `docs/context/milestone-context-<M>.md` for the assigned milestone.
5. The assigned ticket under `docs/milestones/<M>/tickets/`.
6. `docs/verification/VERIFY-REGISTER.md` entries relevant to the ticket.
7. `docs/workflow/model-effort-guide.md`.
8. `docs/specs/mes-signaux.md` for Signal/plan/UI/crafting-orchestration work.
9. Only additional specialized specifications, ADRs, tests and source files
   needed by the ticket.

Do not load all historical milestone/ticket documents for routine work.

The assigned ticket is the implementation contract for scope/behavior. The
model-effort guide is authoritative for model effort, dedicated Plan-mode use,
review-model selection and PR Draft blocking when older annotations conflict.

## Active product model

Canonical flow:

`Renseignement -> Opportunité -> Plan -> Étapes -> Signal -> Réconciliation -> Résultat`

A **Signal** is an opportunity sufficiently safe, profitable, relevant and
compatible with the owner's current state to justify a concrete manual action.

Internal no-action states (`WAIT`, `HOLD`, `KEEP BID`, `REVIEW`, harmless
undercut/outbid, etc.) normally stay silent on the primary feed unless a concrete
corrective action is required.

Target primary navigation:

`Mes Signaux / Artisanat / Réglages`

Scanner, dashboard, raw inventory, retained history, personal learning and order
books are supporting engines/evidence rather than primary user destinations.

## UI language requirement

**All user-facing UI/UX labels and text introduced or modified by active product
work MUST be French.** This includes:

- navigation;
- actions/buttons;
- headings/helper text;
- errors and degraded states;
- empty states;
- notification text;
- explanations/reasons;
- accessibility labels/announcements.

Internal code symbols, API/type/database names, structured reason identifiers,
test identifiers and developer diagnostics may remain English. Proper nouns and
technical identifiers may remain canonical where translating them would reduce
clarity.

Do not make financial/recommendation logic depend on localized strings. Prefer
structured semantics/reason codes with French rendering at the presentation
boundary.

## Active product boundaries

- The local application MAY use a dedicated ArenaNet API key for verified
  read-only account/Trading Post endpoints.
- The application MUST NOT automate gameplay, place/cancel/update Trading Post
  orders, craft automatically, or use ArenaNet mutation/write operations.
- API keys, authorization headers, credentials and secret values MUST NOT appear
  in source, browser code/storage, frontend payloads, logs, fixtures, prompts,
  tests, commits, pull requests or SQLite.
- The browser MUST NOT be a secret-store client. It receives safe status and
  structured application results from the loopback host.
- Normal hosting MUST bind only to explicit loopback addresses and validate
  approved Host values; loopback binding alone is not DNS-rebinding protection.
- Production frontend/API MUST be same-origin. Development CORS MUST allow only
  exact configured trusted origins. State-changing local endpoints MUST have
  cross-origin/anti-forgery protection independent of CORS.
- All ArenaNet access MUST pass through typed gateway abstractions. Feature code
  MUST NOT construct ArenaNet URLs directly.
- External DTOs MUST remain separate from domain/application models.
- Authoritative money arithmetic MUST use integer copper. Do not use binary
  floating point for purchase cost, sale value, fees, profit, cost basis or
  position sizing.
- Trading fees and financial policy MUST be centralized. Never add scattered
  `0.85`, `15%` or equivalent fee shortcuts to features/React.
- Unknown historical cost basis and unknown crafting input economics MUST remain
  explicit; never treat them as free.
- Recommendation/plan/reconciliation logic MUST be deterministic, explainable
  and testable. Do not add a runtime LLM or opaque ML ranking model.
- A high score/ROI/profit MUST NOT override failed hard safety, evidence,
  liquidity, risk, freshness or resource constraints.
- Market statistics describe observed history; they MUST NOT be presented as
  guarantees or fabricated for windows with insufficient coverage.
- Tests are required for changed financial, accounting, persistence,
  statistical, security, recommendation, plan or reconciliation behavior. Never
  weaken a test just to obtain a pass.
- Material uncertainty about ArenaNet endpoints, permissions, quotas, schemas,
  timestamps, cache behavior, fees or limits belongs in VERIFY. Do not invent
  external contracts.

## Architecture direction

Target runtime:

`React UI -> loopback ASP.NET Core host/API -> Application/Analytics -> SQLite + typed ArenaNet gateway`

Keep Domain and Analytics deterministic/framework-independent where practical.
Keep persistence and HTTP in Infrastructure. Keep endpoints thin. React renders
structured results and interactions; it does not duplicate financial truth.

For plan/shadow work, verified ArenaNet state and locally recorded unconfirmed
execution events are separate authorities. The shadow is provisional/reversible;
verified state eventually wins and material contradiction pauses for explicit
reconciliation rather than guessing.

See:

- `docs/specs/project-spec.md`
- `docs/specs/mes-signaux.md`
- `docs/specs/trading-rules.md`
- `docs/architecture/architecture.md`
- `docs/architecture/data-model.md`
- `docs/adr/ADR-010-personal-local-first-pivot.md`

## Current roadmap discipline

Issue #98 and `docs/milestones/INDEX.md` define the explicit execution order.
Within current M21, **do not infer execution order from numeric issue/ticket
numbers**. The owner-approved transition sequence intentionally places
#129-#133 before the remaining crafting tickets #93/#94.

One implementation ticket remains the normal unit of work.

## Ticket and session discipline

Default rule: **one implementation ticket = one implementation session**.

Use one isolated worktree/branch. Inspect Git state, make a short in-session plan
of no more than five steps, implement one coherent ticket, run required
validation, inspect the diff, commit/push/open a PR, write the required report,
and stop.

Dedicated Plan mode is used only when `docs/workflow/model-effort-guide.md`
calls for it, the owner explicitly requests it, or a genuine unresolved
high-consequence product/architecture/security/financial/scope ambiguity exists.

If a genuine ambiguity, contradiction or owner/product decision cannot be
resolved from the repository, pause and ask the owner with a recommended choice
and concise alternatives. Do not ask the owner to decide routine technical
choices already authorized by the ticket.

Do not begin the next ticket merely because context remains. Durable handoff
comes from Git, issue/ticket contracts, tests, docs and verified live GitHub
state rather than chat memory.

A separate focused fix/test session is allowed if the implementation session
cannot finish safely, but it remains scoped to the same ticket.

## Review policy

There are two review paths. **Risk class alone does not select Sol.**

### NORMAL

For tickets not explicitly listed in the active Sol gate:

- use risk-based Terra effort from `docs/workflow/model-effort-guide.md`;
- run an independent Terra review subagent/check inside the implementation run
  when supported;
- R0/R1 review may use Terra Medium by default; R2/R3 uses Terra High by
  default, escalating for material uncertainty/findings;
- run all ticket-required tests and CI;
- a second owner-triggered review session is not a default merge requirement;
- do not put obsolete blanket `R3 requires fresh flagship XHigh` wording in a
  NORMAL PR.

Escalate to Sol only if unresolved high-consequence ambiguity remains, an
important review finding remains uncertain, or the owner explicitly requests it.

### SOL-GATED

Only the explicit active list in `docs/workflow/model-effort-guide.md` requires
separate fresh Sol XHigh review.

For those tickets:

1. implement/fix with Terra High by default;
2. create the PR as **Draft**;
3. complete required local validation and let required GitHub CI go green;
4. only then spend the fresh separate Sol XHigh review session using
   `.codex/skills/tyrian-pr-review/SKILL.md`;
5. keep the PR Draft while findings remain;
6. fix confirmed findings with Terra High and rerun affected validation/CI;
7. use a targeted fresh Sol re-review focused on prior findings/diff/regression
   risk unless fixes materially broadened scope;
8. mark Ready only after APPROVE and green validation;
9. the owner still makes the final merge decision.

If quota is exhausted, preserve Draft/handoff and do not bypass the required
Sol gate.

## Model and reasoning guidance

- R0 mechanical/docs/repository maintenance: Terra Medium by default.
- R1 normal product implementation: Terra Medium by default; escalate to High
  when materially cross-layer/stateful/ambiguous.
- R2 complex cross-layer/stateful implementation: Terra High by default.
- R3 financial/data/security/statistical/recommendation/state-authority work:
  Terra High by default.
- Dedicated Plan mode: exception for genuine ambiguity/high consequence or owner
  request; a short in-session plan remains mandatory.
- NORMAL review: same-run independent Terra review/check plus required tests/CI.
- Separate Sol XHigh: only explicit active Sol gates or explicit escalation.
- Max: only for unresolved ambiguity after XHigh or explicit owner request.

Never lower tests/correctness because a cheaper review path is used.

## Decision gates reserved for the owner

Pause and present concise options before materially changing:

- product scope/milestone intent;
- an accepted ADR/architectural boundary;
- ArenaNet permission/key requirements;
- canonical fee/accounting policy;
- persistence retention, destructive migration or data clearing behavior;
- network exposure beyond loopback;
- production/paid external dependencies;
- automated gameplay/Trading Post boundaries;
- release, merge, destructive branch/data deletion or public deployment.

Routine implementation choices inside an accepted ticket do not require owner
confirmation.

## Completion report — required for every implementation ticket

End with a short report containing:

1. **Functional summary** — 2–6 plain-language sentences describing what the
   ticket now lets the user/project do and what deliberately remains outside it.
2. **Files changed** — concise paths/components.
3. **Acceptance criteria** — passed/not passed with exceptions.
4. **Validation** — exact commands/checks/results.
5. **VERIFY changes** — added/resolved/carried-forward IDs.
6. **Risks or limitations** — material remaining issues only.
7. **Owner decision required** — `None` if no decision remains.
8. **Pull request URL**.
9. **Review path** — NORMAL or SOL-GATED.

Before delivery:

- ensure PR body contains `Closes #<issue-number>`;
- ensure actual GitHub milestone matches the issue milestone when tooling allows;
- reconcile GitHub live state and the `CURRENT.md` generated block;
- do not rewrite durable `CURRENT.md` narrative for routine handoff;
- keep SOL-GATED PRs Draft until the required review gate is satisfied.

Do not merge the pull request.
