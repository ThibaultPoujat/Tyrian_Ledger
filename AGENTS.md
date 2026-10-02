# Tyrian Ledger — Coding Agent Instructions

## Mission

Build Tyrian Ledger as a **local-first second-screen personal Guild Wars 2 profit
assistant**. The product continuously turns account/market evidence into the
smallest useful set of concrete manual actions for the owner to perform in game.

The primary daily Signals surface is displayed as **`Signaux`**. The 0.1
economic focus is Trading Post flipping/trading plus crafting. Existing
investment-position/staged-exit infrastructure is preserved, but investment
discovery/seasonality is deferred from the 0.1 critical path.

The approved target is Windows 1920×1080 landscape. Session objectives include
active work and liquid-gold deadlines. The owner is the first user, with possible
friend/public distribution later; local-first does not prohibit distribution.

The coding model is a development tool only. It is never part of runtime
financial truth and never executes gameplay or Trading Post actions.

M0-M11 are historical milestones. The active local-first pivot begins at M12.
Follow current source-of-truth documents rather than inferring product intent
from old files.

## Repository and displayed language

Repository-facing artifacts use **English**. This includes documentation
filenames and ordinary documentation prose, code/type/API/database/migration
identifiers, internal domain terminology, enums/reason codes, test identifiers,
configuration keys, branch/ticket technical names when newly created, and
machine-readable state.

French is the product's **display language**. Exact French strings may appear in
documentation when they specify or quote what the user sees.

All user-facing UI/UX labels and text introduced or modified by active product
work MUST be French, including navigation, actions/buttons, headings/helper text,
errors/degraded states, empty states, notification text, explanations/reasons and
accessibility labels/announcements.

Proper nouns and technical identifiers may remain canonical where translating
them would reduce clarity. Do not make financial/recommendation logic depend on
localized strings. Prefer structured semantics/reason codes with French rendering
at the presentation boundary.

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

Authority is split by concern; there is no single authority for every generated
handoff field:

- **Operational delivery state:** GitHub merged PRs, issue open/closed state, and milestone assignment/title.
- **Execution order / next valid ticket:** issue #98 and `docs/milestones/INDEX.md`. Numeric issue ordering is not authoritative.
- **Review effort and gates:** `docs/workflow/model-effort-guide.md`, including the active explicit Sol-gate list.

Issue #98 and `docs/milestones/INDEX.md` must agree on execution order. If they
conflict, treat that as a source-of-truth contradiction to repair; do not silently
choose one or infer order from issue numbers.

`CURRENT.md` contains durable context plus a clearly delimited generated
live-state block. That block combines the authorities above and is a derived
handoff/cache view, not an independent source of truth. TKT-M21-S01 / #129
maintains it deterministically after merges without AI/model quota.

At the **start of every implementation session**:

1. inspect the assigned issue/PR and the relevant owning authority for each generated field;
2. compare those authoritative values with the generated live block in `CURRENT.md`;
3. repair stale generated fields from their owning authority before relying on them;
4. never let GitHub issue numbering override the explicit roadmap order and never infer a Sol gate from risk class/issue metadata;
5. do not ask the owner to maintain `CURRENT.md` manually during normal work.

Never rewrite durable `CURRENT.md` narrative merely to update live ticket state.

## Read order for an implementation ticket

1. Reconcile/read the `CURRENT.md` generated fields against their owning authorities.
2. This file.
3. `docs/context/permanent-context.md`.
4. `docs/context/milestone-context-<M>.md` for the assigned milestone.
5. The assigned ticket under `docs/milestones/<M>/tickets/`.
6. `docs/verification/VERIFY-REGISTER.md` entries relevant to the ticket.
7. `docs/workflow/model-effort-guide.md`.
8. `docs/specs/approved-product-direction.md` and relevant sections of `docs/specs/signals.md` for product/plan work; for UI work, also read `docs/ux/ux.md`, `docs/ux/tyrian-ledger-visual-reference.md` and **open the relevant approved PNGs** before coding. Non-UI tickets need not load all images.
9. Only additional specialized specifications, ADRs, tests and source files
   needed by the ticket.

Do not load all historical milestone/ticket documents for routine work.

The assigned ticket is the implementation contract for scope/behavior. The
model-effort guide is authoritative for model effort, dedicated Plan-mode use,
review-model selection and PR Draft blocking when older annotations conflict.

## Active product model

Canonical domain lifecycle:

`Opportunity -> Plan -> Steps -> Reconciliation -> Outcome`

Market/account intelligence is upstream evidence that feeds Opportunity
discovery. A **Signal is not a lifecycle stage**; it is a presentation/eligibility
concept for an opportunity/plan action sufficiently safe, profitable, relevant
and compatible with the owner's current state to justify surfacing a concrete
manual action.

Internal no-action states (`WAIT`, `HOLD`, `KEEP BID`, `REVIEW`, harmless
undercut/outbid, etc.) normally stay silent on the primary feed unless a concrete
corrective action is required.

Displayed primary navigation:

`Signaux / Plans / Bilan`, with `Réglages` at the bottom

Scanner, dashboard, raw inventory, retained history, personal learning and order
books are supporting engines/evidence rather than primary user destinations.

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
- `docs/specs/signals.md`
- `docs/specs/trading-rules.md`
- `docs/architecture/architecture.md`
- `docs/architecture/data-model.md`
- `docs/adr/ADR-010-personal-local-first-pivot.md`

## Current roadmap discipline

Issue #98 and `docs/milestones/INDEX.md` define explicit order. The approved
corrective packages are in `docs/milestones/approved-delivery-plan.md`.
Foundations, B1 and B2 are merged. C04 / #177 prepares B3 in
`docs/milestones/M22/batch-03.md`: #178 → #179 → #180 → #181 → #182. The first
four contracts are conditionally Ready; #182 additionally requires interactive
Windows evidence/access and is environment-gated. The remaining map retains
P02E2/P03C2 and later Planned units.
After each owner merge, the next prepared contract can start if its entry
conditions still hold; no Astra planning session is required between those tickets.
G01 / #150 is a tracking gate, **not an executable coding ticket**. At the batch
boundary, prepare the next 3–5 ready contracts from
`docs/milestones/remaining-delivery-map.md`. Do not implement all packages in one
Goal or skip directly to #96. Old feature merges are not proof of audit closure.

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
comes from Git, issue/ticket contracts, tests, docs and verified authoritative
repository/GitHub state rather than chat memory.

Batch planning does not authorize automatic multi-ticket execution. One reusable
Goal selects the first conditionally Ready contract in the explicit queue, verifies
its dependencies against merged PRs and their evidence, then stops after that PR.
Report routine evidence in the PR and the assigned batch row; avoid duplicate
logs/status narratives. Escalate to Astra only at the batch exit or a real contract
ambiguity, invalid dependency or repeated blocker defined by the model guide.

A separate focused fix/test session is allowed if the implementation session
cannot finish safely, but it remains scoped to the same ticket.

## Review policy

Use `docs/workflow/model-effort-guide.md` as the single model/effort authority.
Every implementation ticket uses `.codex/skills/tyrian-pr-review/SKILL.md` for an
independent review; same-run review is sufficient for NORMAL when supported.
Use one focused reviewer, not a default worker swarm. Self-review alone is not
independent evidence. Risk alone does not select the separate Sol XHigh gate.

SOL-GATED PRs remain Draft until required validation/CI are green and the fresh
independent Sol XHigh review approves. Missing review capability/quota means
preserve Draft and handoff, not silently bypass the gate. Owner merges.

UI PRs require actual 1920×1080 application screenshots compared with the
relevant six approved reference PNGs. The reviewer must open both. Missing
visual evidence or material unapproved design drift is an Important finding.
See `docs/ux/tyrian-ledger-visual-reference.md` for exact coverage and overrides.
Do not substitute the old Sites prototype, earlier concepts or a new redesign.

## Model and reasoning guidance

The model-effort guide owns defaults. The owner requests Astra for precise
planning/checkpoints and the implementation model explicitly assigned by the
model-effort guide (GPT-6.1 Sol High for B3). C04 alone is the owner-authorized
GPT-6.1 Sol Medium checkpoint exception instead of Astra, approved 2026-10-02.
Use `docs/workflow/goal-session.md` for the one-ticket Goal contract. Do not
weaken validation for a cheaper model or claim quota behavior the host has not
established. Stop after the assigned ticket's PR/handoff, never auto-merge.

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
- reconcile the generated `CURRENT.md` fields against their owning authorities;
- do not rewrite durable `CURRENT.md` narrative for routine handoff;
- keep SOL-GATED PRs Draft until the required review gate is satisfied.

Do not merge the pull request.
