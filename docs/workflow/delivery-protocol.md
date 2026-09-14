# Codex Delivery Protocol

This document contains Git/GitHub delivery rules. The ticket and `AGENTS.md`
define implementation scope and decision gates. `docs/workflow/model-effort-guide.md`
is authoritative for the current quota-aware model effort, planning, and review policy.

## Branch

Create one branch per ticket:

`ticket/<TICKET_NAME>-<short-kebab-title>`

A bootstrap/pivot branch explicitly authorized by the owner may use an
equivalent descriptive name, but normal M12+ ticket work follows the ticket
pattern.

## Commits

Every ticket commit, including commits on an authorized bootstrap/pivot branch,
starts with the exact ticket identifier:

`[TKT-Mxx-yy] Short description`

Keep commits logically reviewable. Do not rewrite unrelated history.

## Pull request

Every completed implementation ticket produces a PR before completion.

PR title:

`[TICKET_NAME] Short title`

PR body must include:

- ticket and milestone;
- the exact GitHub issue number;
- `Closes #<issue-number>` so GitHub closes the implementation issue when the PR is merged into the default branch;
- the active review path: `NORMAL` or `SOL-GATED`;
- **functional summary** in plain language (2-6 sentences);
- relevant specification/architecture/ADR references;
- acceptance-criteria status;
- validation commands/checks and results;
- decisions/ADRs, if any;
- VERIFY changes;
- risks/limitations;
- deliberately out-of-scope follow-up work.

Before delivery, set the pull request's actual GitHub milestone to the same milestone assigned to the implementation issue. The textual milestone entry in the PR body does not replace the GitHub milestone field. If the active tool cannot set the milestone, report that limitation explicitly.

## Review paths

### NORMAL

All tickets not listed in the explicit active Sol gate in `docs/workflow/model-effort-guide.md` use the normal Plus-constrained path:

- risk-based Terra planning/implementation from the model-effort guide rather than High for every ticket;
- independent Terra review subagent/check **inside the implementation run** when the coding environment supports it;
- Medium review by default for R0/R1 and High for R2/R3, escalating when findings/uncertainty justify it;
- all ticket-required tests/checks and CI;
- PR may be opened Ready for Review;
- no second owner-triggered review session or separate Sol review is a merge gate unless the implementation/reviewer reports unresolved high-consequence ambiguity or the owner explicitly escalates.

Do **not** write legacy statements such as `R3 requires fresh flagship XHigh review` in a NORMAL PR body. Risk class and review model are separate concepts.

### SOL-GATED

Only the explicit active ticket list in `docs/workflow/model-effort-guide.md` is Sol-gated.

For a SOL-GATED ticket:

1. Implement/fix with Terra High by default.
2. Open the PR as **Draft** and state `Review path: SOL-GATED` in the PR body.
3. Complete required local validation and push the implementation.
4. Let required GitHub CI finish. If CI is red, fix with Terra High and revalidate before spending a Sol review session.
5. With required validation/CI green, run a fresh separate Sol XHigh review using `.codex/skills/tyrian-pr-review/SKILL.md`.
6. If the review requests changes, use Terra High for fixes, rerun affected validation/CI, and keep the PR Draft.
7. Run a **targeted fresh Sol re-review** focused on prior findings, the changed diff, and regression risk. Require a full Sol re-review only if fixes materially broadened the reviewed authority/scope.
8. After Sol returns APPROVE and required validation remains green, mark the PR **Ready for Review**.
9. The owner performs the final merge decision.

The Draft state is the merge blocker. Do not rely on the owner remembering a checklist or ticket number. If quota is exhausted, preserve the Draft/handoff and resume later rather than bypassing the gate.

## Review handoff

NORMAL tickets should complete their independent review with a Terra review
subagent/check in the implementation run when supported. A second separate
review session remains optional and should not be started merely out of habit.

SOL-GATED tickets require the separate fresh Sol review described above after
required validation/CI is green. The Sol reviewer should report findings first
and must not broaden ticket scope.

If the owner requests fixes, keep them on the same ticket branch and do not add
next-ticket features. Re-run affected validation and make the review status clear.

## Delivery checklist

- [ ] Acceptance criteria satisfied or explicit blocker recorded.
- [ ] Required functional summary written.
- [ ] Relevant validation performed.
- [ ] Diff reviewed for scope expansion, secrets, data/migration risk, and stale contradictory docs.
- [ ] Every ticket commit uses the exact ticket prefix.
- [ ] Branch pushed.
- [ ] PR created and existence verified.
- [ ] PR body contains `Closes #<issue-number>`.
- [ ] PR GitHub milestone matches the implementation issue milestone, or the tooling limitation preventing assignment is reported explicitly.
- [ ] PR body names the correct review path without obsolete blanket R3/XHigh language.
- [ ] NORMAL: same-run independent Terra review subagent/check completed when supported and CI is green.
- [ ] SOL-GATED: required validation/CI was green before Sol review; PR remains Draft until Sol XHigh APPROVE; only then is it marked Ready.
- [ ] Any SOL-GATED fixes were revalidated before targeted fresh Sol re-review.
- [ ] PR not merged by the coding/review agent.
- [ ] VERIFY register current.
- [ ] `CURRENT.md` updated by the implementation/delivery agent when the active handoff changes.
