## Ticket and milestone

- Ticket:
- GitHub issue:
- Closes #<issue-number>
- Milestone:
- Review path: NORMAL / SOL-GATED

<!--
Set the pull request's actual GitHub milestone to match the implementation issue.
Review-path selection is defined by docs/workflow/model-effort-guide.md.
Do not infer SOL-GATED from R3 alone and do not paste legacy blanket R3/XHigh wording.
SOL-GATED PRs must be opened as Draft and remain Draft until the required separate Sol review returns APPROVE.
-->

## Functional summary

In 2-6 plain-language sentences, describe what the user/project can now do that
it could not do before, what important behavior changed, and what remains
deliberately outside this ticket. Do not substitute a file list.

## Acceptance criteria

- [ ] Criterion met

Map every ticket criterion to pass/fail/blocker evidence.

## Changes and references

List the relevant specification, architecture, ADR, security, testing, UX, and
VERIFY documents implemented or validated.

## Validation

List exact commands/checks and results. For high-risk financial/data/security
work, include edge/regression evidence rather than only happy-path tests.

## VERIFY, risks, and follow-up

- VERIFY items added/updated/resolved:
- Risks or limitations:
- Deliberately out-of-scope follow-up work:
- Owner decision still required (or `None`):

## Review result

- NORMAL: independent Terra review subagent/check completed; required tests/CI green.
- SOL-GATED: keep this PR Draft until a fresh separate Sol XHigh review returns APPROVE; record the verdict here before marking Ready.

<!-- Keep only the review-result line that applies to this PR. -->

## Owner review

- [ ] Functional summary/result matches the intended outcome.
- [ ] Acceptance criteria are satisfied or an explicit blocker is understood.
- [ ] CI/relevant validation is passing.
- [ ] Any required durable decision has been made.
