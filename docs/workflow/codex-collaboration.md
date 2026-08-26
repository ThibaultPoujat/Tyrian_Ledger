# Owner and Codex Collaboration

## Roles

The owner supplies product intent and makes durable product decisions. Codex
turns that intent into a small ticket, implements it, validates it, and opens a
pull request. The owner does not need to write code, but remains the approver
for the decision gates in `AGENTS.md` and for merging a pull request.

## Default configuration

Use Codex with **GPT-5.6 Terra** and **High** reasoning effort for normal
implementation. Use **XHigh** for an independent final review of security,
financial, architectural, or hard-debugging work. Use Medium only for small,
mechanical documentation or repository-maintenance tasks. Do not use Max or
Ultra by default; compare them against XHigh only when the normal process has
failed to achieve a required quality bar.

## Ticket lifecycle

1. The owner gives a functional brief using
   `docs/workflow/functional-brief-template.md`.
2. Codex identifies the milestone, proposes or updates one ticket, and asks
   only for a real decision gate.
3. An implementation task works in one worktree and creates a reviewable PR.
4. A fresh Codex review task checks the PR against its ticket, relevant ADRs,
   tests, security boundaries, and the diff. It may add corrective commits to
   the same branch.
5. CI passes. The owner reads the short functional report, verifies that the
   outcome matches the brief, and merges the PR.

Do not ask one task to implement an entire milestone. Do not run overlapping
implementation tasks in one worktree. Separate work is safe to parallelize
only when it has different worktrees and no shared files or decisions.

## How to start a Codex task

For an existing ticket, use a short request such as:

> Implement `TKT-M1-02`. Read `AGENTS.md` and the ticket first. If the ticket
> is stale, ambiguous, or requires a durable owner decision, explain that
> before changing code; otherwise implement it, validate it, and open a PR.

There is no corresponding prompt file. The root instructions and the ticket
are the execution contract. For new work, provide a completed functional brief
instead; Codex will turn it into one small ticket before implementation.

Treat completed tickets as history. Treat a not-yet-started ticket as a useful
backlog contract: refresh its external facts, dependencies, and acceptance
criteria immediately before beginning it instead of maintaining duplicate
prompts or preemptively rewriting every future ticket.

## What the owner should provide

Describe what should be true for the player, not a technical solution. State
the desired outcome, examples, priority, non-goals, and any constraint that is
important to you. Screenshots, Guild Wars 2 examples, and a ranked list of
trade-offs are useful when available.

## Questions Codex must ask

Codex should proceed autonomously for normal ticket work. It must pause for
the owner when the change requires one of the durable decisions listed in
`AGENTS.md`, when requirements conflict, or when acceptance criteria cannot be
tested safely. It should present a concise recommendation and the consequences
of each option rather than asking broad technical questions.

## Current workflow cleanup

The M0 Qwen/MTPLX records are historical evidence from the retired local-agent
workflow. Current source Markdown, tickets, ADRs, and Git state are
authoritative for Codex work.
