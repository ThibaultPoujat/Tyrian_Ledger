# Owner and Codex Collaboration

## Roles

The owner defines the product outcome, approves durable decisions, evaluates the
functional result, and merges. Codex turns one accepted ticket into a reviewable
implementation and does not merge its own PR.

## Model selection

The authoritative quota-aware policy is
`docs/workflow/model-effort-guide.md`.

Default references:

- R0 mechanical/docs maintenance: **Terra Medium**;
- R1 normal product implementation: **Terra Medium**, escalating to High when materially cross-layer/stateful/ambiguous;
- R2 complex cross-layer/stateful implementation: **Terra High**;
- R3 financial/data/security/statistical/recommendation authority: **Terra High**;
- NORMAL review: same-run independent Terra review subagent/check plus required tests/CI, using Medium for R0/R1 and High for R2/R3 by default;
- SOL-GATED review: fresh separate Sol XHigh only for the explicit active ticket list in the model-effort guide, after required validation/CI is green.

Risk class still controls test depth and reviewer focus, but **R3 does not by
itself require Sol or a separate review session**.

## Ticket lifecycle

1. The owner selects one ticket or provides a functional brief.
2. The implementation session reads `CURRENT.md`, `AGENTS.md`, current context,
   the ticket, and the model-effort guide.
3. It makes a short in-session plan. Dedicated Plan mode is used only when the model-effort guide or a genuine unresolved owner decision warrants it.
4. If a genuine ambiguity/contradiction cannot be resolved from the repository, Codex asks the owner with a recommended choice and concise alternatives; routine technical choices stay autonomous.
5. It implements only that ticket in an isolated branch/worktree and validates.
6. It runs the ticket's review path:
   - NORMAL: independent Terra review subagent/check in the same implementation run when supported;
   - SOL-GATED: create/keep the PR Draft, get required validation/CI green, then hand off to a fresh Sol XHigh review session.
7. Confirmed findings are fixed within ticket scope and revalidated. SOL-GATED fixes use Terra High, then a targeted fresh Sol re-review unless the fix materially broadened scope.
8. The owner checks the functional summary/behavior and merges only after the required gate is satisfied.
9. GitHub closes the issue through `Closes #<issue-number>` and the agent-managed
   `CURRENT.md` handoff keeps repository state current.

Do not ask one task to implement an entire milestone. Do not combine consecutive
tickets because context remains.

## Starting an implementation task

For an existing ticket, a short prompt is enough:

> Implement issue #XX exactly. Follow `CURRENT.md`, `AGENTS.md`, the ticket, and
> the repository workflow. If you find a genuine ambiguity, contradiction, or
> owner decision that cannot be resolved from the repository, ask me before
> implementing and give your recommended choice plus concise alternatives. Build
> only this ticket, validate it, run the required review path, open/update the PR,
> and stop. Do not merge.

The repository, not the prompt, carries the architecture, model effort, Plan-mode
policy, review gate, milestone, closing keyword, and handoff rules.

## Starting a separate review task

A separate review session is required only for SOL-GATED tickets or an explicit
escalation. For SOL-GATED work, start it only after required validation/CI is
green. Use a fresh context:

> Review the current SOL-GATED PR using the `tyrian-pr-review` skill. Read the
> repository source of truth and exact ticket first. Findings first. Do not
> modify files or merge.

For NORMAL tickets, the independent Terra review subagent/check inside the
implementation workflow is sufficient by default. Do not create a second owner-
triggered review session merely out of habit.

## Questions Codex should ask

Proceed autonomously for implementation choices already authorized by the
ticket. Pause only for a durable owner decision in `AGENTS.md`, contradictory
requirements, destructive behavior outside the ticket, or a true blocker.

When pausing, present a recommendation plus concrete options/consequences rather
than a broad technical question.

## Quota pressure

Quota pressure changes scheduling, not correctness. If a required SOL-GATED
review cannot run because allowance is exhausted, keep the PR Draft and resume
when quota returns or the owner deliberately chooses another supported allowance
option. Never silently replace a required Sol review with a cheaper reviewer.

## Historical workflow note

M0-M11 records explain the project's evolution. M12+ is the active local-first
roadmap. Earlier workflow statements requiring Terra High for every ticket,
actual Plan mode for every implementation, or a fresh separate flagship review
for every R3 ticket are superseded by the central quota-aware model-effort guide.
