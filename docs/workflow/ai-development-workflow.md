# Local Coding-Agent Development Workflow

## Purpose

Qwen is the local development agent operated through Pi/MTPLX. It is not part of the application runtime and is not the project's decision-maker.

The workflow is designed for a local 32 GB Apple Silicon machine: small active contexts, fresh sessions, deterministic validation, and Git-based hand-offs.

## Context model

Each ticket session loads the minimum context:

- `docs/context/permanent-context.md`
- current milestone context
- `docs/verification/VERIFY-REGISTER.md`
- one ticket under `docs/milestones/<M>/tickets/`
- the matching prompt under `docs/milestones/<M>/prompts/`
- specialized documents/source files only when required

Deep specification documents remain reference material. Do not inject the whole project documentation set into every session.

## Ticket versus session

A ticket is a project unit. A session is a bounded execution slice.

A ticket may use several sessions:

1. implementation;
2. focused tests;
3. review/final validation.

Start a new session when context is becoming large/noisy, the work phase changes, compaction has already occurred, or the remaining work can be recovered from Git and the ticket.

Do not run concurrent Qwen sessions for ordinary work on the local 32 GB machine.

## Context target

Use approximately 16K active context as the default local target. A larger model context is not a reason to preserve a long session. Prefer a new session over an oversized conversation.

## Standard session lifecycle

1. Start a fresh Pi session.
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

## Human review

Human review remains mandatory for API permissions, credentials/authentication, API request policy, financial formulas, persistence schema decisions, security behavior, project scope, and architectural decisions.

Qwen must never merge its own pull request.
