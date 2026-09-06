# Codex Development Workflow

## Purpose

Codex implements one bounded ticket at a time. The owner supplies functional
intent and makes durable product decisions. The application runtime remains
deterministic and contains no application LLM.

## Context model

Each implementation session loads the minimum durable context:

1. `CURRENT.md`;
2. `AGENTS.md`;
3. `docs/context/permanent-context.md`;
4. current milestone context;
5. one assigned ticket;
6. relevant `docs/verification/VERIFY-REGISTER.md` entries;
7. `docs/workflow/model-effort-guide.md`;
8. specialized source/spec/ADR files only when required.

Do not load all historical milestones or the entire specification tree for a
routine ticket.

## Ticket versus session

A ticket is the unit of product work. Default rule:

**one implementation ticket = one implementation session.**

A separate session may be used for focused fixes/tests if necessary, but must
remain scoped to the same ticket. Do not begin the next ticket merely because
context remains.

## Standard implementation lifecycle

1. Read the ticket and minimum context.
2. Inspect current Git/repository state and relevant VERIFY items.
3. Make a plan of at most five steps.
4. Implement only the ticket outcome.
5. Run focused validation, then broader checks when justified.
6. Inspect the diff for scope expansion, secrets, data-loss risk, and stale docs.
7. Run the review path selected by `model-effort-guide.md`.
8. Commit/push/open the PR according to `delivery-protocol.md`.
9. Write the required completion report including the short functional summary.
10. Stop.

The next session recovers from repository state; it does not require previous
chat history.

## Review paths

Review-model selection is defined centrally in
`docs/workflow/model-effort-guide.md`. **R3 by itself does not require Sol.**

### NORMAL

For tickets not listed in the explicit Sol gate:

- Terra High planning/implementation is the default;
- use an independent Terra review subagent/check when supported;
- run all ticket-required tests and CI;
- a separate review session is optional, not a merge requirement;
- escalate to Sol only for unresolved high-consequence ambiguity, uncertain
  Important/Blocker findings, or explicit owner request.

### SOL-GATED

For the explicit Sol-gated ticket list:

- implement/fix with Terra High by default;
- open the PR as Draft;
- use `.codex/skills/tyrian-pr-review/SKILL.md` in a fresh separate Sol XHigh
  session;
- keep the PR Draft while findings remain;
- after APPROVE and green validation, mark the PR Ready for Review;
- owner performs the final merge.

Draft state is the merge blocker. Do not rely on the owner remembering the
Sol-gate list manually.

## VERIFY and BLOCKED

`VERIFY` means an external fact is unresolved but safe work can continue without
assuming it. `BLOCKED` means missing/contradictory information makes requested
work unsafe or technically impossible.

Do not stop merely because a non-blocking external fact is uncertain. Record it
and proceed with assumptions clearly isolated from financial truth.

## Anti-loop policy

- Maximum five planning steps.
- Prefer execution over repeated summaries.
- Do not reread unchanged files more than twice without new reason.
- Do not retry the same failed operation more than twice without changing the
  approach.
- Stop after the coherent ticket slice is delivered.

## Testing policy

For code tickets, test changed behavior and dangerous boundaries around it. Run
narrow relevant tests first; broaden when integration risk justifies it.

R3 tickets still require edge/regression cases appropriate to their authority,
regardless of whether review path is NORMAL or SOL-GATED. The quota-aware policy
changes reviewer/model selection, not correctness standards.

Never weaken/delete a test merely to obtain green CI.

## Architecture and ADRs

Create/update an ADR only for a durable cross-cutting decision. Ordinary ticket
implementation does not require a new ADR. Superseded ADRs remain historical
records and are not active instructions.

## Required functional summary

Every implementation ticket ends with a short plain-language summary (normally
2-6 sentences) answering:

- What can the user/project do now that it could not do before?
- What important behavior changed?
- What remains deliberately outside this ticket?

Do not substitute a file list or technical changelog for this summary.
