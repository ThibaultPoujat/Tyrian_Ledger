# Tyrian Ledger - Codex Instructions

## Mission

Build a local, read-only Guild Wars 2 Trading Post analysis application. Codex
is the development agent only; it is never part of the application runtime or
the source of financial truth.

## Read order for a ticket

1. This file.
2. `docs/context/permanent-context.md`.
3. The current milestone context.
4. `docs/verification/VERIFY-REGISTER.md`.
5. One assigned ticket under `docs/milestones/<M>/tickets/`.
6. Only the specialized documents and source files needed to satisfy that
   ticket.

The ticket is the task contract. Do not load the whole specification or
unrelated tickets unless the assigned work cannot be resolved without them.

## Non-negotiable boundaries

- The application is read-only: no Guild Wars 2 API writes, gameplay
  automation, or Trading Post automation.
- Never put credentials or tokens in source, browser code/storage, logs,
  fixtures, prompts, tests, commits, or pull requests.
- All Guild Wars 2 access goes through the typed gateway. Feature code must
  not construct ArenaNet URLs.
- Keep external DTOs separate from domain models.
- Financial calculations are deterministic and use integer copper; no
  floating-point money arithmetic.
- Do not add an application LLM or alter an ADR silently.
- Tests are required for changed behaviour. Never weaken or remove a test just
  to obtain a pass.
- Record material external uncertainty in the VERIFY register. Do not invent
  API fields, permissions, quotas, endpoints, or behaviour.

## Working style

- Work on one ticket in one isolated worktree/branch. Do not concurrently edit
  the same files from multiple tasks.
- Use a short plan (at most five steps), implement a coherent slice, validate
  it, inspect the diff, then stop. A ticket may use separate implementation,
  test, and review tasks.
- Use Git state, tests, tickets, and ADRs as the hand-off; do not depend on a
  long conversation for project memory.
- Prefer targeted reads, lean prompts, and a fresh review task over duplicated
  instructions or repeated summaries.

## Authority and decision gates

An assigned ticket authorizes in-scope local edits, tests, commits, branch
pushes, and a pull request following `docs/workflow/delivery-protocol.md`.
The owner must decide before a change that materially alters product scope,
an ADR, Guild Wars 2 permissions or live-key use, financial policy, persistence
schema/data retention, network exposure, a paid or production dependency, or
release/merge/deletion behaviour. Report the decision required and the viable
options; do not guess.

## Codex configuration

The normal owner-selected Codex configuration is GPT-5.6 Terra with High
reasoning effort. Use a fresh XHigh review task only for security-sensitive,
financial, architectural, or unusually difficult work. Model selection does
not relax any project boundary.

## Completion report

Report: user-visible outcome, files changed, acceptance-criteria status,
validation commands/results, VERIFY changes, risks/limitations, required owner
decision (if any), and the pull-request URL. Do not merge the pull request.
