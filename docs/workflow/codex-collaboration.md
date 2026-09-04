# Owner and Codex Collaboration

## Roles

The owner defines the product outcome, approves durable decisions, evaluates the
functional result, and merges. Codex turns one accepted ticket into a reviewable
implementation and does not merge its own PR.

## Model selection

Choose model/effort by risk rather than milestone number. The detailed matrix is
`docs/workflow/model-effort-guide.md`.

Default references:

- routine/mechanical: GPT-5.6 Terra Medium/High;
- normal implementation: Terra High;
- complex implementation: Terra High or GPT-5.6 Sol High;
- financial/accounting/persistence/security/private-data/statistical/
  recommendation/network-exposure/architecture-authority review: fresh
  flagship XHigh, normally Sol XHigh; GPT-6 Astra may replace Sol when it is
  available to the owner.

Independent context is more important than asking the implementation session to
self-review at higher effort.

## Ticket lifecycle

1. The owner selects one ticket or provides a functional brief.
2. The implementation session reads `CURRENT.md`, `AGENTS.md`, current context,
   and that ticket.
3. It implements only that ticket in an isolated branch/worktree, validates,
   opens a PR, and writes a short functional summary.
4. A **fresh** review session uses the Tyrian PR review skill to check the PR
   against the ticket, canonical docs, relevant ADRs, tests, security/data
   boundaries, and diff.
5. Confirmed findings are fixed within ticket scope and revalidated.
6. CI passes. The owner checks the functional summary/behavior and merges.
7. `CURRENT.md` is updated when the active project state/next ticket changes.

Do not ask one task to implement an entire milestone. Do not combine consecutive
tickets because the model still has context budget.

## Starting an implementation task

For an existing ticket:

> Implement `TKT-Mxx-yy`. Read `CURRENT.md`, `AGENTS.md`, the milestone context,
> and the ticket first. Work only on that ticket in an isolated branch/worktree.
> Validate it, open a PR, include the required short functional summary, and
> stop. If a real owner decision gate is required, present the decision and
> options; otherwise proceed autonomously.

## Starting a review task

Use a fresh context:

> Review the PR for `TKT-Mxx-yy` using the `tyrian-pr-review` skill. Do not rely
> on the implementation conversation. Check the ticket acceptance criteria,
> canonical docs, diff, tests, financial/security/data invariants, and VERIFY
> state. Report findings by severity with evidence, include the functional
> summary and acceptance-criteria matrix, and do not make edits unless I ask for
> review-and-fix.

## Questions Codex should ask

Proceed autonomously for implementation choices already authorized by the
ticket. Pause only for a durable owner decision in `AGENTS.md`, contradictory
requirements, destructive behavior outside the ticket, or a true blocker.

When pausing, present a recommendation plus concrete options/consequences rather
than a broad technical question.

## Historical workflow note

M0-M11 records explain the project's evolution. M12 is the active pivot. Old
static Pages instructions are not authoritative merely because their source
files/code still exist before TKT-M12-02.
