# Coding Agent Execution Rules

These rules define how Pi/Qwen executes one ticket without turning the chat history into the project's state.

## Context discipline

For a ticket, load only:

1. `docs/context/permanent-context.md`
2. `docs/context/milestone-context-<M>.md`
3. `docs/verification/VERIFY-REGISTER.md`
4. the assigned ticket under `docs/milestones/<M>/tickets/`
5. the matching prompt under `docs/milestones/<M>/prompts/`
6. only specialized documents or source files explicitly required by the ticket

Do not read the complete project specification for routine work. Read it only when a specific unresolved requirement cannot be answered from the smaller context.

Prefer targeted file/range reads over broad scans. Do not repeatedly reread unchanged files.

## Ticket versus session

A ticket is a project work item. A session is one bounded execution slice.

A ticket MAY require multiple fresh sessions, for example:

1. implementation;
2. focused tests;
3. review/fix/validation.

Start a fresh session when:

- the work phase changes materially;
- the context becomes large or noisy;
- the model has already compacted;
- the remaining work can be recovered from Git state and the ticket;
- the agent starts repeating earlier reasoning.

Git commits and the working tree are the durable hand-off. Do not depend on chat history for continuity.

On the 32 GB local development machine, do not run concurrent Qwen sessions for ordinary ticket work.

## Context target

The model/runtime may support a larger context, but local development should target approximately 16K active context unless a ticket explicitly requires more and the machine remains stable.

Do not increase context merely to avoid starting a fresh session.

## Scope

Implement only the assigned ticket. Within the current session, implement one coherent slice of that ticket.

Do not implement future tickets, redesign the application, add an application LLM, or perform unrelated cleanup.

## VERIFY versus BLOCKED

Use `VERIFY` when an external fact is uncertain but the ticket can safely continue without treating the fact as true.

Use `BLOCKED` only when missing or contradictory information makes the requested work technically impossible or unsafe.

When uncertain:

1. do not invent the fact;
2. register it as VERIFY when material;
3. continue with work that does not depend on it.

Do not repeat the same investigation after sufficient evidence has already been recorded.

## Execution over planning

Planning must be brief: at most five steps.

After understanding the current slice, execute it. Do not repeatedly summarize the task, restate acceptance criteria, or describe edits that have not been performed.

Do not ask for approval to execute when the ticket already instructs the agent to execute.

## Repetition guard

- Do not read the same unchanged file more than twice.
- Do not repeat the same analysis more than twice without new evidence.
- Do not retry the same failed operation more than twice.
- If an operation still cannot be completed after two attempts, stop and report the exact blocker.
- Never enter a read -> summarize -> reread -> resummarize loop.

## Validation

Validate the work slice directly against the affected acceptance criteria.

For code:

1. run the narrowest relevant tests first;
2. then broader validation when useful;
3. run formatting/analyzers/build when applicable;
4. inspect the diff for scope expansion and secrets.

For documentation-only tickets, validate documents, references, claims, VERIFY status, and acceptance criteria. Do not invent executable tests for behavior that does not exist.

Never weaken or delete tests to obtain a pass.

## Completion and hand-off

A session is complete when its coherent work slice is validated and recorded in the working tree/Git state.

A ticket is complete only when its full acceptance criteria and delivery protocol are complete.

If ticket work remains, report exactly what remains and stop. The next session starts fresh.

Do not select or implement the next ticket.
