# Owner and Codex Collaboration

## Roles

The owner defines the product outcome, approves durable decisions, evaluates the
functional result, and merges. Codex turns one accepted ticket into a reviewable
implementation and does not merge its own PR.

## Model selection

The authoritative quota-aware policy is
`docs/workflow/model-effort-guide.md`.

Default references:

- routine/mechanical: GPT-5.6 Terra Medium/High;
- normal and complex implementation: **Terra High by default**;
- NORMAL review: independent Terra review subagent/check plus required tests/CI;
- SOL-GATED review: fresh separate Sol XHigh, only for the explicit ticket list
  in the model-effort guide.

Risk class still controls test depth and reviewer focus, but **R3 does not by
itself require Sol or a separate review session**.

## Ticket lifecycle

1. The owner selects one ticket or provides a functional brief.
2. The implementation session reads `CURRENT.md`, `AGENTS.md`, current context,
   the ticket, and the model-effort guide.
3. It implements only that ticket in an isolated branch/worktree and validates.
4. It runs the ticket's review path:
   - NORMAL: independent Terra review subagent/check;
   - SOL-GATED: create/keep the PR Draft and hand off to a fresh Sol XHigh
     review session.
5. Confirmed findings are fixed within ticket scope and revalidated.
6. CI passes. A SOL-GATED PR remains Draft until Sol APPROVE, then becomes Ready.
7. The owner checks the functional summary/behavior and merges.
8. GitHub closes the issue through `Closes #<issue-number>` and the agent-managed
   `CURRENT.md` handoff keeps repository state current.

Do not ask one task to implement an entire milestone. Do not combine consecutive
tickets because context remains.

## Starting an implementation task

For an existing ticket, a short prompt is enough:

> Implement issue #XX exactly. Follow `CURRENT.md`, `AGENTS.md`, the ticket, and
> the repository workflow. Use Plan mode first, then build only this ticket,
> validate it, run the required review path, open/update the PR, and stop. Do
> not merge.

The repository, not the prompt, carries the architecture, review gate, milestone,
closing keyword, and handoff rules.

## Starting a separate review task

A separate review session is required only for SOL-GATED tickets or an explicit
escalation. Use a fresh context:

> Review the PR using the `tyrian-pr-review` skill. Read the repository source of
> truth and exact ticket first. Findings first. Do not modify files or merge.

For NORMAL tickets, the independent Terra review subagent/check inside the
implementation workflow is sufficient by default.

## Questions Codex should ask

Proceed autonomously for implementation choices already authorized by the
ticket. Pause only for a durable owner decision in `AGENTS.md`, contradictory
requirements, destructive behavior outside the ticket, or a true blocker.

When pausing, present a recommendation plus concrete options/consequences rather
than a broad technical question.

## Historical workflow note

M0-M11 records explain the project's evolution. M12+ is the active local-first
roadmap. Earlier workflow statements requiring a fresh separate flagship review
for every R3 ticket are superseded by the central quota-aware model-effort guide.
