# Tyrian Ledger — Coding Agent Rules

Read `docs/context/permanent-context.md` first.

Then read only:

- the current milestone context;
- the assigned ticket under `docs/milestones/<M>/tickets/`;
- the matching prompt under `docs/milestones/<M>/prompts/`;
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
- stop after the current session's coherent work slice is complete.

## Session discipline

A ticket is a work item, not a single conversation.

- Prefer a fresh Pi session for each coherent implementation, test, or review phase.
- Do not try to carry a long conversation across phases when the repository and Git state already contain the required context.
- Treat commits and the working tree as the durable hand-off between sessions.
- On the 32 GB local development machine, do not run concurrent Qwen sessions for ordinary ticket work.
- Prefer keeping the active context comfortably below the model limit; 16K is the default target for local agent work.

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
- After the requested work and validation for the current session slice are complete, STOP.
