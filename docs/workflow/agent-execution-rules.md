# Qwen Agent Execution Rules

These rules govern how the local coding agent executes a single ticket.
They are intentionally short. Deep project requirements remain in the specification,
architecture, ADRs, security documentation, and tickets.

## Context discipline

For a ticket, load only:

1. `docs/context/permanent-context.md`
2. `docs/context/milestone-context-Mx.md`
3. `docs/verification/VERIFY-REGISTER.md`
4. the assigned ticket
5. the assigned prompt
6. only explicitly relevant specialized documents or source files

Do not read the complete project specification unless the ticket cannot be completed
from the smaller context and a specific requirement is missing.

Do not repeatedly reread unchanged files.

## Scope

One ticket is one execution unit.

Implement only the assigned ticket. Do not implement future tickets, redesign the
application, add an application LLM, or make unrelated cleanup changes.

## VERIFY versus BLOCKED

Use `VERIFY` when an external fact is uncertain but the ticket can safely continue
without treating the fact as true.

Use `BLOCKED` only when missing or contradictory information makes the requested
work technically impossible or unsafe to implement.

When a fact is uncertain:

1. do not invent it;
2. register it as VERIFY when material;
3. continue with work that does not depend on the unresolved fact.

Do not repeatedly investigate the same uncertainty after sufficient evidence has
already been recorded.

## Execution over planning

Planning must be brief: at most five steps.

After understanding the ticket, execute the requested work. Do not repeatedly
summarize the task, restate acceptance criteria, or describe edits that have not
been performed.

## Repetition guard

- Do not read the same unchanged file more than twice.
- Do not repeat the same analysis more than twice without new evidence.
- Do not retry the same failed operation more than twice.
- If an operation still cannot be completed after two attempts, stop and report the
  exact blocker.
- Never enter a read -> summarize -> reread -> resummarize loop.

## Validation

Validate the acceptance criteria directly.

For code changes:

1. run the narrowest relevant tests first;
2. then run broader validation when useful;
3. run formatting/analyzers/build when applicable;
4. inspect the diff for scope expansion and secrets.

For documentation-only tickets, do not invent executable tests merely to satisfy a
generic testing rule. Validate the document, references, claims, VERIFY status, and
acceptance criteria instead.

Never weaken or delete a test just to make it pass.

## Completion

A ticket is complete only when its acceptance criteria are satisfied, validation has
been performed, the diff is reviewed, and the delivery protocol has been completed.

Do not claim completion without evidence.

Do not choose or implement the next ticket. Stop after the current ticket.
