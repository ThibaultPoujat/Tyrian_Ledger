# Reusable One-Ticket Goal

The same prompt works throughout a prepared batch. Model selection happens in the
host UI; this document does not configure routing, limits or automatic reviewers.
Use **GPT-6.1 Sol Medium** for #168, then **GPT-6.1 Sol High** for #169–#171.
Select `gpt-6.1-sol` and the ticket's effort in the host before each Goal.
Keep implementation/fixes for one ticket together; use a new implementation
context for the next ticket. Review uses a separate independent context; #170
requires fresh GPT-6.1 Sol XHigh only after green CI.

## Copyable implementation prompt

```text
Implement exactly the next Ready implementation ticket in
ThibaultPoujat/Tyrian_Ledger on current develop.

Read AGENTS.md. Reconcile CURRENT.md with GitHub delivery state, issue #98,
docs/milestones/INDEX.md and docs/workflow/model-effort-guide.md.
Use docs/milestones/M22/batch-02.md for the current prepared queue.
Use GPT-6.1 Sol at the selected ticket's effort: Medium for #168, High for
#169–#171. If the active host model/effort cannot match the assignment, preserve
a handoff rather than silently substituting or claiming this prompt changes it.
Select only the first open implementation in explicit order; verify its
predecessor merges, final-head CI/review and ticket entry conditions. Distinguish reviewed code
from later evidence-only commits; code changes require affected re-review. Closed
issues alone are not evidence of delivery. If the next item is #150 or a
prerequisite/contract is invalid, stop with the exact checkpoint/blocker.

Read the selected ticket and only relevant specs, source and VERIFY entries.
Use one isolated branch/worktree and a short in-session plan. Implement its
acceptance vectors, run required checks and preserve all stated non-goals.
For UI work, open the ticket's approved original PNGs before coding; provide
actual 1920×1080 screenshots and a reference comparison.

Use $tyrian-pr-review with the model/effort assigned by the model guide.
NORMAL: obtain one independent review within this run when supported.
SOL-GATED: keep Draft until green CI and fresh independent Sol XHigh approval.
Missing review capability or visual evidence means Draft with a precise handoff,
not self-approval or a cheaper substitute. Fix in this ticket and re-review only
the affected diff/findings unless the authority/scope materially changed.

Commit with the selected ticket prefix. Open a PR to develop with Closes #issue
and actual M22 milestone. Put concise test/review evidence in the PR and update
only the assigned batch evidence row with links/head/preview instructions.
Stop after this PR/handoff. Do not merge, implement a second ticket, create a
new planning ticket, or reinterpret the entire tracking gate as one coding task.
```

After an owner merge, reuse this prompt. No new Astra Goal-writing session is
needed while the next contract is prepared and its entry conditions hold.
After #167 preparation merges, B2 order is #168 → #169 → #170 → #171 → #150 checkpoint.
Update this batch-manifest pointer only at the next batch preparation.

## Review handoff when a separate session is required

```text
Use $tyrian-pr-review to independently review PR [number] at its current head
against develop. Read its assigned contract and the model-effort guide.
Use the required review model/effort; do not silently substitute.
For SOL-GATED work, confirm required validation/CI are green before the full pass.
Inspect the diff and acceptance evidence independently. For UI, open the original
approved references and actual screenshots. No edits or merge.
Return findings, acceptance evidence and APPROVE / CHANGES REQUESTED / BLOCKED.
```

## Batch checkpoint

At #150, Astra uses the completed batch's evidence and owner preview feedback to
assess integrated behavior and promote the next 3–5 units from
[remaining-delivery-map.md](../milestones/remaining-delivery-map.md).
Update both order authorities, model assignments and this reusable handoff in
one preparation. Keep #150 open until all P01–P06 exits and owner progression
approval. A package, fixture preview or planning document is not completion.

An early checkpoint is justified by an invalid contract/dependency, an unresolved
owner decision or two failed attempts at the same blocker. Routine fixes,
renamed symbols, formatting and ordinary review findings do not require Astra.
