# Codex Development Workflow

## Purpose

Codex implements the work; the owner supplies functional intent and makes
durable product decisions. The application runtime remains deterministic and
contains no application LLM.

## Context model

Each ticket session loads the minimum context:

- `AGENTS.md`
- `docs/context/permanent-context.md`
- current milestone context
- `docs/verification/VERIFY-REGISTER.md`
- one ticket under `docs/milestones/<M>/tickets/`
- specialized documents/source files only when required

Deep specification documents remain reference material. Do not inject the whole project documentation set into every session.

## Ticket versus session

A ticket is a project unit. A session is a bounded execution slice.

A ticket may use several sessions:

1. implementation;
2. focused tests;
3. review/final validation.

Start a new session when context is becoming large/noisy, the work phase changes, compaction has already occurred, or the remaining work can be recovered from Git and the ticket.

Do not run concurrent tasks that touch the same files or need the same durable
decision. Use separate worktrees for independent work.

## Standard session lifecycle

1. Start a Codex task from a functional brief and one ticket.
2. Read the assigned ticket and minimal context.
3. Review relevant VERIFY items.
4. Inspect the current repository/Git state.
5. Make a plan of no more than five steps.
6. Execute one coherent work slice.
7. Run focused validation.
8. Review the diff.
9. Commit or hand off according to the delivery protocol.
10. Stop.

The next session recovers from the repository state; it does not inherit the previous chat history.

## VERIFY and BLOCKED

`VERIFY` means an external fact is unresolved but safe progress can continue without assuming it.

`BLOCKED` means missing or contradictory information makes the requested work technically impossible or unsafe.

Do not stop merely because something is uncertain. Record VERIFY and continue when possible.

## Anti-loop policy

- Maximum five planning steps.
- Prefer execution over repeated summaries.
- Do not reread unchanged files more than twice.
- Do not repeat analysis without new evidence.
- Do not retry a failed operation more than twice.
- Never enter a read -> summarize -> reread -> resummarize loop.
- Stop after the current coherent work slice.

## Testing policy

For code tickets, test changed behavior and run the narrowest relevant tests first. Broader validation follows when useful.

For documentation tickets, validate documents and acceptance criteria instead of inventing executable tests for behavior that does not exist.

Never weaken or delete tests merely to obtain a passing result.

## Architecture and ADRs

Create or update an ADR only for a durable cross-cutting architectural decision. Ordinary ticket implementation does not require a new ADR.

## Owner review

The owner decides API permissions, credentials/authentication, API request
policy, financial formulas, persistence schema decisions, security behaviour,
project scope, and architectural decisions. See
`docs/workflow/codex-collaboration.md` for the normal implementation/review
loop. Codex must never merge its own pull request.
