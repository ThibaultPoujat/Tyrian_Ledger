# Tyrian Ledger — Qwen Coding Agent Rules

Read `docs/context/permanent-context.md` first.

Then read only:

- the current milestone context;
- the assigned ticket;
- the assigned ticket prompt;
- `docs/verification/VERIFY-REGISTER.md`;
- specialized documents explicitly required by the ticket.

Follow `docs/workflow/agent-execution-rules.md` for execution behavior and
`docs/workflow/delivery-protocol.md` for Git/GitHub delivery.

## Hard project boundaries

Never:

- invent GW2 API fields, permissions, quotas, endpoints, or behavior;
- add write-capable GW2 operations;
- add gameplay or Trading Post automation;
- commit secrets;
- bypass the single API gateway;
- weaken or delete tests to make a ticket pass;
- add an application LLM;
- silently alter an ADR or other durable architecture decision;
- make unrelated changes without justification.

Always:

- preserve money-as-integer-copper semantics;
- keep external API DTOs separate from domain models;
- report assumptions and unresolved verification items;
- maintain `docs/verification/VERIFY-REGISTER.md` for material uncertainty;
- run the smallest relevant validation first;
- stop after the assigned ticket is complete.

## VERIFY discipline

`docs/verification/VERIFY-REGISTER.md` is the project-level index.

For every ticket:

1. review relevant existing VERIFY items;
2. do not treat unresolved items as facts;
3. add newly discovered material uncertainties;
4. update affected existing items when new evidence changes them;
5. mark an item RESOLVED only when supporting evidence is recorded in the ticket or another authoritative document;
6. preserve resolved entries for history;
7. reference relevant VERIFY IDs in the ticket report.

Use `VERIFY` when uncertainty does not prevent safe progress. Use `BLOCKED` only
when the missing or contradictory information makes the requested work impossible
or unsafe.

## Anti-loop rules

- Do not repeatedly summarize the same state.
- Do not read the same unchanged file more than twice.
- Do not repeat the same analysis more than twice without new evidence.
- Do not retry the same failed operation more than twice.
- If an operation still fails after two attempts, stop and report the exact blocker.
- After the requested changes and validation are complete, STOP.
