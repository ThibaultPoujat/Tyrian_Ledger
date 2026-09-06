# Tyrian Ledger - Coding Agent Instructions

## Mission

Build Tyrian Ledger as a **local-first personal Guild Wars 2 Trading Post
assistant**. The application helps one player understand personal performance,
research markets, allocate capital, and decide what manual action to take next.
The coding model is a development tool only. It is never part of runtime
financial truth and never executes gameplay or Trading Post actions.

M0-M11 are historical milestones. The active pivot starts at M12. Follow the
active source-of-truth documents rather than inferring architecture from old
files.

## Read order for an implementation ticket

1. `CURRENT.md`.
2. This file.
3. `docs/context/permanent-context.md`.
4. `docs/context/milestone-context-<M>.md` for the assigned milestone.
5. The assigned ticket under `docs/milestones/<M>/tickets/`.
6. `docs/verification/VERIFY-REGISTER.md`.
7. `docs/workflow/model-effort-guide.md` for the active model/review gate.
8. Only the specialized specifications, ADRs, tests, and source files needed to
   satisfy that ticket.

The ticket is the implementation contract for scope/behavior. The model-effort
guide is authoritative for **review-model selection and PR Draft blocking** when
older ticket annotations conflict with the current quota-aware policy.

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

## Review policy

There are two review paths. **Risk class alone does not select the model.**

### NORMAL

For every ticket not explicitly listed in the Sol gate in
`docs/workflow/model-effort-guide.md`:

- use Terra High for planning/implementation by default;
- run an independent Terra review subagent/check before delivery when supported
  by the coding environment;
- run all ticket-required tests and CI;
- a separate Sol session is not a merge requirement;
- do not put obsolete blanket `R3 requires fresh flagship XHigh` wording in the
  PR body.

Escalate to Sol only if Terra reports unresolved high-consequence ambiguity, an
important review finding remains uncertain, or the owner explicitly requests it.

### SOL-GATED

Only the explicit Sol-gate ticket list in `docs/workflow/model-effort-guide.md`
requires a separate fresh Sol XHigh review.

For those tickets:

- implement/fix with Terra High by default;
- create the PR as **Draft**;
- use `.codex/skills/tyrian-pr-review/SKILL.md` in a fresh separate Sol XHigh
  review session;
- keep the PR Draft while findings remain;
- after APPROVE and green validation, the reviewer/fix handoff may mark it Ready
  for Review;
- the owner still makes the final merge decision.

The Draft state is the merge blocker so the owner does not have to remember the
Sol-gate list manually.

## Model and reasoning guidance

- Mechanical docs/repository maintenance: Terra Medium/High.
- Normal and complex implementation: **Terra High by default**.
- Normal review: independent Terra review subagent/check plus required tests/CI.
- Separate Sol XHigh: only the explicit Sol-gate tickets or an explicit
  escalation under the rules above.
- Max: only for unresolved ambiguity after normal XHigh review or an explicit
  owner request.

Never lower testing or correctness standards because the cheaper review path is
used. The quota-aware policy changes **who reviews**, not the acceptance criteria
or validation burden.

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
9. **Review path** — NORMAL or SOL-GATED.

Before delivery, ensure the PR body contains `Closes #<issue-number>`, the actual
GitHub milestone matches the issue milestone, and `CURRENT.md` reflects the
valid handoff. SOL-GATED PRs must still be Draft at implementation handoff.

Do not merge the pull request.
