# Qwen + MTPLX Development Workflow

## Purpose

Qwen is the local development agent. It is not the product decision-maker and is not
part of the application runtime.

This document is the human-readable overview of the workflow. Detailed agent behavior
and GitHub delivery rules live in:

- `docs/workflow/agent-execution-rules.md`
- `docs/workflow/delivery-protocol.md`

## Context model

The repository deliberately separates deep reference material from lightweight agent
context.

### Always-loaded context

- `docs/context/permanent-context.md`
- current `docs/context/milestone-context-Mx.md`
- `docs/verification/VERIFY-REGISTER.md`
- one assigned ticket
- one assigned prompt

### Loaded only when relevant

- project specification;
- architecture;
- ADRs;
- security;
- rate-limit policy;
- endpoint matrix;
- testing strategy;
- UX documentation;
- source files.

Do not load the complete project specification for every ticket.

## Ticket lifecycle

1. Start a fresh agent session for the ticket.
2. Read the minimum context listed above.
3. Review relevant VERIFY items.
4. Inspect repository state.
5. Make a brief implementation plan.
6. Implement only the ticket.
7. Validate the acceptance criteria.
8. Review the diff.
9. Update the VERIFY register when material uncertainty was discovered.
10. Complete the Git/GitHub delivery gate.
11. Stop. The next ticket starts in a new session.

## VERIFY and blockers

`VERIFY` means an external fact is unresolved but work can continue safely without
assuming it is true.

`BLOCKED` means missing or contradictory information makes the requested work
impossible or unsafe.

Qwen should not stop merely because a fact is uncertain. It should register the
uncertainty and continue wherever possible.

## Anti-loop policy

Qwen must prefer execution over repeated planning.

- Maximum five planning steps.
- Do not repeatedly restate acceptance criteria.
- Do not reread unchanged files more than twice.
- Do not repeat an analysis without new evidence.
- Do not retry a failed operation more than twice.
- After two unsuccessful attempts, report the exact blocker.
- Never enter a read -> summarize -> reread -> resummarize loop.
- Stop when the ticket is complete.

## Testing policy

For code tickets, test behavior changes and run the narrowest relevant tests first.
Broader validation follows when useful.

For documentation-only tickets, validate the documents and acceptance criteria rather
than inventing executable tests where no implementation exists yet.

Never weaken or delete a test simply to obtain a passing result.

## Architecture and ADRs

An ADR is an Architecture Decision Record: a short record of a durable architectural
decision, its context, alternatives, chosen option, and consequences.

Do not create an ADR for ordinary ticket implementation. Create/update one only when a
durable, cross-cutting architectural decision is required.

## Human review

Human review remains mandatory for changes involving:

- API permissions;
- credentials/authentication;
- API request policy;
- financial formulas;
- persistence schema decisions;
- security behavior;
- project scope;
- architectural decisions.

Qwen must not merge its own pull requests.
