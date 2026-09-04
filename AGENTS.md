# Tyrian Ledger - Coding Agent Instructions

## Mission

Build Tyrian Ledger as a **local-first personal Guild Wars 2 Trading Post
assistant**. The application helps one player understand personal performance,
research markets, allocate capital, and decide what manual action to take next.
The coding model is a development tool only. It is never part of runtime
financial truth and never executes gameplay or Trading Post actions.

M0-M11 are historical milestones. The active pivot starts at M12. Static Pages
artifacts and documentation may still exist while M12 retires them; follow the
active source-of-truth documents rather than inferring architecture from old
files.

## Read order for an implementation ticket

1. `CURRENT.md`.
2. This file.
3. `docs/context/permanent-context.md`.
4. `docs/context/milestone-context-<M>.md` for the assigned milestone.
5. The assigned ticket under `docs/milestones/<M>/tickets/`.
6. `docs/verification/VERIFY-REGISTER.md`.
7. Only the specialized specifications, ADRs, tests, and source files needed to
   satisfy that ticket.

The ticket is the implementation contract. Do not load every historical ticket
or the entire repository documentation unless the assigned work genuinely
requires it.

## Active product boundaries

- The local application MAY use a dedicated ArenaNet API key for verified
  read-only account/Trading Post endpoints.
- The application MUST NOT automate gameplay, place/cancel/update Trading Post
  orders, or use ArenaNet mutation/write operations.
- API keys, authorization headers, credentials, and secret values MUST NOT
  appear in source, browser code/storage, frontend payloads, logs, fixtures,
  prompts, tests, commits, pull requests, or SQLite.
- The browser MUST NOT be a secret-store client. It receives safe status and
  structured application results from the loopback host.
- Normal hosting MUST bind only to explicit loopback addresses and validate
  approved Host values (`AllowedHosts` or equivalent); loopback binding alone
  is not DNS-rebinding protection.
- Production frontend/API MUST be same-origin. Development CORS MUST allow only
  exact configured trusted origins. State-changing local endpoints MUST have
  cross-origin request/anti-forgery protection independent of CORS.
- All ArenaNet access MUST pass through typed gateway abstractions. Feature code
  MUST NOT construct ArenaNet URLs directly.
- External DTOs MUST remain separate from domain/application models.
- Authoritative money arithmetic MUST use integer copper. Do not use floating
  point for purchase cost, sale value, fee, profit, cost basis, or position
  sizing.
- Trading fees and financial policy MUST be centralized. Never add scattered
  `0.85`, `15%`, or equivalent fee shortcuts to features or React.
- Unknown historical cost basis MUST remain explicit; never treat it as free.
- Recommendation logic MUST be deterministic, explainable, and testable. Do not
  add an application LLM or opaque ML ranking model.
- Market statistics describe observed history; they MUST NOT be presented as
  guarantees or fabricated for windows with insufficient coverage.
- Tests are required for changed financial, accounting, persistence,
  statistical, security, or recommendation behavior. Never weaken a test just
  to obtain a pass.
- Material uncertainty about ArenaNet endpoints, permissions, quotas, schemas,
  timestamps, cache behavior, or fees belongs in the VERIFY register. Do not
  invent external contracts.

## Architecture direction

Target runtime:

`React UI -> loopback ASP.NET Core host/API -> Application/Analytics -> SQLite + typed ArenaNet gateway`

Keep Domain and Analytics deterministic and framework-independent where
practical. Keep persistence and HTTP in Infrastructure. Keep endpoints thin.
React renders structured results and user interactions; it does not duplicate
financial truth.

See:

- `docs/specs/project-spec.md`
- `docs/specs/trading-rules.md`
- `docs/architecture/architecture.md`
- `docs/architecture/data-model.md`
- `docs/adr/ADR-010-personal-local-first-pivot.md`

## Ticket and session discipline

Default rule: **one implementation ticket = one implementation session**.

Use one isolated worktree/branch. Inspect Git state, make a short plan of no
more than five steps, implement one coherent ticket, run the required
validation, inspect the diff, commit/push/open a PR, write the required report,
and stop.

Do not begin the next ticket in the same implementation session. Durable handoff
comes from Git, the ticket, `CURRENT.md`, tests, and ADRs rather than chat
memory.

A separate focused test/fix session is allowed when the implementation session
cannot finish safely, but it must remain scoped to the same ticket and record a
clear handoff.

## Independent review

Every implementation PR receives a **fresh review session** that did not
implement the change. Use `.codex/skills/tyrian-pr-review/SKILL.md` as the
standard review playbook.

The default review is read-only: inspect the ticket, source of truth, diff,
validation evidence, and relevant tests; report findings before making any
corrective edits. If the owner explicitly requests review-and-fix, corrective
commits may follow without expanding ticket scope.

The reviewer must prioritize correctness over style and pay special attention
to secret boundaries, integer money, fee semantics, migration/data loss,
idempotent sync, accounting reconstruction, statistical sufficiency,
recommendation explanations, and acceptance-criteria coverage.

## Model and reasoning guidance

Model availability changes; the ticket contract is authoritative regardless of
model name. Choose effort by risk:

- **Mechanical docs/repository maintenance:** GPT-5.6 Terra, Medium or High.
- **Normal implementation:** GPT-5.6 Terra, High.
- **Complex cross-layer implementation:** GPT-5.6 Sol, High when available; a
  strong equivalent is acceptable.
- **Financial, accounting, persistence migration, security, private-data,
  statistical, recommendation, network-exposure, or architecture-authority
  work:** classify as R3 and use a fresh flagship-model review, normally
  GPT-5.6 Sol at XHigh. If a stronger flagship model such as GPT-6 Astra is
  available to the owner, it MAY replace Sol for these review gates.
- Use Max only when XHigh has produced unresolved ambiguity or the ticket is
  unusually difficult; do not pay for Max mechanically.

Never lower testing or review standards because a stronger model was selected.
Never assume a larger model makes an independent review unnecessary.

## Decision gates reserved for the owner

Pause and present concise options before materially changing:

- product scope or milestone intent;
- an accepted ADR or architectural boundary;
- ArenaNet permission/key requirements;
- canonical fee/accounting policy;
- persistence retention, destructive migration, or data clearing behavior;
- network exposure beyond loopback;
- production/paid external dependencies;
- automated gameplay/Trading Post boundaries;
- release, merge, destructive branch/data deletion, or public deployment.

Routine implementation choices inside an accepted ticket do not require owner
confirmation.

## Completion report - required for every implementation ticket

End with a short report containing:

1. **Functional summary** — 2-6 sentences in plain language describing what the
   ticket now lets the user do or what project capability changed. Avoid file
   lists as the summary.
2. **Files changed** — concise paths/components.
3. **Acceptance criteria** — passed/not passed with any exception.
4. **Validation** — exact commands/checks and results.
5. **VERIFY changes** — added/resolved/carried forward IDs.
6. **Risks or limitations** — only material remaining issues.
7. **Owner decision required** — `None` if no decision remains.
8. **Pull request URL**.

Do not merge the pull request.
