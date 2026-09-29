# One-Ticket Goal Handoff

A Goal is a bounded execution container, not permission to implement a whole
milestone autonomously. Use the host interface's available model selector; this
file does not claim to configure model routing, background execution or usage
limits. Instructions in Git make the handoff independent of chat memory.

## Ready definition

A coding Goal starts only when its issue and repository contract have: merged
dependencies, a concrete outcome, non-goals, exact acceptance examples, test
commands, relevant file entry points, a risk class, model/review path and no
unresolved owner decision blocking the work. A tracking gate is not a Ready
coding ticket. A feature package is not automatically a ticket.

For a UI ticket, also specify screen IDs from the six-image manifest, required
states and screenshot/interaction acceptance. The agent must open the referenced
PNGs before implementation. Never ask Luna to infer the latest design from chat.

## Current implementation Goal — P01B (after #153 merges)

P01A / #149 merged in PR #152. Its checkpoint is
[recorded here](../milestones/M22/checkpoint-p01a.md). Use **Luna High** if
available for the next bounded ticket, then paste:

```text
Implement only TKT-M22-P01B / GitHub issue #154 in
ThibaultPoujat/Tyrian_Ledger, based on current develop.

Read AGENTS.md and reconcile CURRENT.md with its authorities. Read the M22
context, docs/milestones/M22/tickets/TKT-M22-P01B.md and model-effort guide.
Confirm checkpoint preparation #153 and resource fix #149 are merged.

Use an isolated branch/worktree. Reproduce the sequential-completion retry
failure, then implement the ticket's step/revision-bound command contract,
atomic durable receipt and state transition, replay/conflict behavior and
minimal client transport wiring. Follow every acceptance vector and non-goal.
Do not rebuild the UI or implement later reconciliation/lifecycle packages.

Use the repository skill $tyrian-pr-review. This ticket is SOL-GATED: open a
Draft PR, run required tests/CI, then obtain fresh independent Sol XHigh review.
If that reviewer is unavailable, leave Draft with the exact review handoff.
Do not impersonate the stronger review or weaken tests/gates for quota reasons.

Commit with [TKT-M22-P01B], target develop, include Closes #154 and actual M22
milestone. Record evidence, commands/results, VERIFY limitations and next action.
Stop after this ticket. Do not merge, implement #150 or start another ticket.
```

The client change is transport-only. The six prototypes remain authoritative
for later visual work; no new screen implementation is part of this ticket.

## Separate review prompt

```text
Use $tyrian-pr-review to review the PR for TKT-M22-P01B / #154 in a fresh
Sol XHigh context. Resolve current PR/base/head, ticket and model guide.
Confirm required validation/CI are green. Independently inspect receipt/state
atomicity, sequential/concurrent retries, stale step/revision, payload conflicts,
restart, cancellation/undo replay, account isolation, committed revisions and
client logical-request identity. Check that #149 resource invariants remain.
Do not edit or merge. Return findings, acceptance evidence and
APPROVE / CHANGES REQUESTED / BLOCKED. Keep Draft for missing required evidence.
```

## Future UI ticket addition

Append the following, replacing the bracketed screen list in the ready ticket:

```text
Open the approved PNGs for [screen IDs] from
 docs/ux/prototypes/2026-09-28/manifest.json.
Follow docs/ux/tyrian-ledger-visual-reference.md, including semantic overrides.
Match the approved shell, layout, density, color and hierarchy. Capture actual
1920×1080 screenshots with deterministic fixtures and compare them to the PNGs.
The independent reviewer must inspect both sets. Keep Draft for missing evidence
or material unapproved drift. Do not replace or regenerate approved references.
```

## Checkpoint after a ticket

The owner merges only after required evidence/review. A later planning session
checks package exits and creates the next Ready child ticket. Update both #98
and INDEX before starting it. Keep #150 open until P01–P06 exits are evidenced;
Neither #149 nor #154 alone completes that gate. Technical record maintenance is the
AI's responsibility; the owner evaluates behavior and product fit.
