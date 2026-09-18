# Codex Delivery Protocol

This document contains Git/GitHub delivery rules. The ticket and `AGENTS.md` define implementation scope and decision gates. `docs/workflow/model-effort-guide.md` is authoritative for the current quota-aware model effort, planning, and review policy.

## Branch

Create one branch per ticket:

`ticket/<TICKET_NAME>-<short-kebab-title>`

A bootstrap/pivot branch explicitly authorized by the owner may use an equivalent descriptive name, but normal M12+ ticket work follows the ticket pattern.

## Commits

Every ticket commit, including commits on an authorized bootstrap/pivot branch, starts with the exact ticket identifier:

`[TKT-Mxx-yy] Short description`

For transition tickets that use an `S` identifier, use that exact identifier, for example `[TKT-M21-S03]`.

Keep commits logically reviewable. Do not rewrite unrelated history.

## Pull request

Every completed implementation ticket produces a PR before completion.

PR title:

`[TICKET_NAME] Short title`

PR body must include:

- ticket and milestone;
- exact GitHub issue number;
- `Closes #<issue-number>` so GitHub closes the implementation issue when the PR is merged into the default branch;
- active review path: `NORMAL` or `SOL-GATED`;
- **functional summary** in plain language (2-6 sentences);
- relevant specification/architecture/ADR references;
- acceptance-criteria status;
- validation commands/checks and results;
- decisions/ADRs, if any;
- VERIFY changes;
- risks/limitations;
- deliberately out-of-scope follow-up work.

Before delivery, set the pull request's actual GitHub milestone to the same milestone assigned to the implementation issue. The textual milestone entry in the PR body does not replace the GitHub milestone field. If the active tool cannot set the milestone, report that limitation explicitly.

## Live handoff and CURRENT.md

The generated `CURRENT.md` live-state block combines multiple authorities:

- **Operational delivery state:** GitHub merged PRs, issue open/closed state, and milestone assignment/title.
- **Execution order / next valid ticket:** issue #98 and `docs/milestones/INDEX.md`; numeric issue ordering is not authoritative.
- **Review effort and gates:** `docs/workflow/model-effort-guide.md`.

Issue #98 and `docs/milestones/INDEX.md` must agree. If they conflict, repair the source-of-truth contradiction rather than silently selecting one.

Before implementation handoff:

1. ensure issue/PR operational metadata is correct in GitHub;
2. ensure `CURRENT.md` durable prose is not rewritten merely to represent a transient handoff;
3. compare each generated live-state field with its owning authority;
4. repair stale generated fields only; do not rewrite execution order from GitHub issue numbering or infer review gates from risk class/issue metadata;
5. if post-merge state will change an operational field, explicitly identify that the deterministic updater/fallback reconciliation is expected to advance it.

TKT-M21-S01 / #129 adds deterministic post-merge maintenance of the generated block. After it lands, normal merge progression should not require owner editing. Per-field session-start reconciliation remains the fallback when automation is stale or failed.

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
7. Run a **targeted fresh Sol re-review** focused on prior findings, changed diff and regression risk. Require a full Sol re-review only if fixes materially broadened reviewed authority/scope.
8. After Sol returns APPROVE and required validation remains green, mark the PR **Ready for Review**.
9. The owner performs the final merge decision.

The Draft state is the merge blocker. Do not rely on the owner remembering a checklist or ticket number. If quota is exhausted, preserve the Draft/handoff and resume later rather than bypassing the gate.

## Review handoff

NORMAL tickets should complete their independent review with a Terra review subagent/check in the implementation run when supported. A second separate review session remains optional and should not be started merely out of habit.

SOL-GATED tickets require the separate fresh Sol review described above after required validation/CI is green. The Sol reviewer should report findings first and must not broaden ticket scope.

If the owner requests fixes, keep them on the same ticket branch and do not add next-ticket features. Re-run affected validation and make review status clear.

## User-facing language check

When a ticket introduces or touches product UI, verify that user-facing labels, actions, messages, errors, empty/degraded states, explanations, notifications and accessibility text are French. Internal identifiers and code symbols may remain English. Do not move financial truth into localized strings.

## Delivery checklist

- [ ] Acceptance criteria satisfied or explicit blocker recorded.
- [ ] Required functional summary written.
- [ ] Relevant validation performed.
- [ ] Diff reviewed for scope expansion, secrets, data/migration risk, stale contradictory docs, and French UI-copy policy where applicable.
- [ ] Every ticket commit uses the exact ticket prefix.
- [ ] Branch pushed.
- [ ] PR created and existence verified.
- [ ] PR body contains `Closes #<issue-number>`.
- [ ] PR GitHub milestone matches the implementation issue milestone, or tooling limitation is reported explicitly.
- [ ] PR body names the correct review path without obsolete blanket R3/XHigh language.
- [ ] NORMAL: same-run independent Terra review subagent/check completed when supported and CI is green.
- [ ] SOL-GATED: required validation/CI was green before Sol review; PR remains Draft until Sol XHigh APPROVE; only then is it marked Ready.
- [ ] Any SOL-GATED fixes were revalidated before targeted fresh Sol re-review.
- [ ] PR not merged by the coding/review agent.
- [ ] VERIFY register current.
- [ ] Each generated `CURRENT.md` field matches its owning authority at handoff, or the deterministic post-merge updater/fallback reconciliation is explicitly expected to advance it.
