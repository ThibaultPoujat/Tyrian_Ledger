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
7. specialized source/spec/ADR files only when required.

Do not load all historical milestones or the entire specification tree for a
routine ticket.

## Ticket versus session

A ticket is the unit of product work. Default rule:

**one implementation ticket = one fresh implementation session.**

A separate session may be used for focused fixes/tests if necessary, but must
remain scoped to the same ticket. The review is always a fresh session.

Do not continue directly into the next ticket merely because context remains.
Do not run concurrent tasks that edit the same files or depend on the same
unresolved decision. Use separate worktrees for genuinely independent work.

## Standard implementation lifecycle

1. Read the ticket and minimum context.
2. Inspect current Git/repository state and relevant VERIFY items.
3. Make a plan of at most five steps.
4. Implement only the ticket outcome.
5. Run focused validation, then broader checks only when justified.
6. Inspect the diff for scope expansion, secrets, data-loss risk, and stale docs.
7. Commit/push/open the PR according to `delivery-protocol.md`.
8. Write the required completion report including the short functional summary.
9. Stop.

The next session recovers from repository state; it does not require previous
chat history.

## Fresh independent review lifecycle

Use `.codex/skills/tyrian-pr-review/SKILL.md`.

The reviewer starts from the ticket, canonical docs, Git diff, and validation
evidence rather than the implementation explanation. Default review is
read-only and findings-first.

For R3 work, use a fresh flagship-model XHigh review. See
`docs/workflow/model-effort-guide.md`.

If findings require fixes, the owner may ask for a scoped review-and-fix pass on
the same ticket branch. Do not turn review into the next feature ticket.

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

For code tickets, test changed behavior and the dangerous boundaries around it.
Run narrow relevant tests first. Broaden once when the integration risk justifies
it.

R3 tickets require edge/regression cases appropriate to their authority. A
financial formula with only happy-path tests is incomplete.

For documentation tickets, validate consistency, links/read order, acceptance
criteria, and stale contradictory guidance rather than inventing runtime tests.

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
