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

## First implementation Goal (after #148 merges)

Use Luna High if available, then paste:

```text
Implement only TKT-M22-P01A / GitHub issue #149 in
ThibaultPoujat/Tyrian_Ledger, based on current develop.

Read AGENTS.md and reconcile CURRENT.md with its owning authorities. Read
its M22 context, docs/milestones/M22/tickets/TKT-M22-P01A.md,
docs/workflow/model-effort-guide.md and relevant VERIFY/spec/source files.
Confirm the P00 dependency #148 is merged. This is a resource-correctness fix,
not permission to implement the rest of P01 or redesign the UI.

Use an isolated branch/worktree and a short plan. Reproduce the duplicate-demand
failure, implement the shared checked aggregation policy in selection and atomic
start, then validate every ticket vector. Preserve existing residual-production,
hard-reserve, account and transaction semantics. Do not invent missing capacity.

Use the repository skill $tyrian-pr-review. This ticket is SOL-GATED: open a
Draft PR, run required tests/CI, then obtain fresh independent Sol XHigh review
when available. Do not impersonate that review from the Luna author context.
If unavailable, leave Draft with exact review handoff. Fix confirmed findings
within this ticket; never weaken tests or change a gate for quota reasons.

Commit with [TKT-M22-P01A], push, open/update a PR targeting develop with
Closes #149 and matching actual milestone. Record acceptance evidence,
commands/results, VERIFY limitations and exact next action. Stop at handoff.
Do not merge, start #150 as coding work, or begin another ticket.
```

This ticket has no UI changes; it does not need all six PNGs in context.

## Separate review prompt

```text
Use $tyrian-pr-review to review the PR for TKT-M22-P01A / #149 in a fresh
Sol XHigh context. Resolve the actual current PR/base/head and current guide.
Confirm required validation/CI are green, then independently inspect the diff
and recompute resource vectors. Review selection and the transactional start
boundary, including existing reservations, overflow and competing starts.
Do not edit or merge. Return findings with evidence, acceptance matrix and
APPROVE / CHANGES REQUESTED / BLOCKED. Keep Draft if required evidence is missing.
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
#149 alone does not complete that gate. Technical record maintenance is the
AI's responsibility; the owner evaluates behavior and product fit.
