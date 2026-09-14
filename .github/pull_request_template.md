## Ticket and milestone

- Ticket:
- GitHub issue:
- Closes #<issue-number>
- Milestone:
- Review path: NORMAL / SOL-GATED

<!--
Set the pull request's actual GitHub milestone to match the implementation issue.
Review-path selection and model effort are defined by docs/workflow/model-effort-guide.md.
Do not infer SOL-GATED from R3 alone and do not paste legacy blanket R3/XHigh wording.
NORMAL review should use the same-run independent Terra review path when supported; do not require a second owner-triggered review session by default.
SOL-GATED PRs must be opened as Draft, complete required validation/CI before the fresh Sol review, and remain Draft until the required Sol review returns APPROVE.
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

- NORMAL: same-run independent Terra review subagent/check completed when supported; required tests/CI green.
- SOL-GATED: required validation/CI green before the fresh separate Sol XHigh review; keep this PR Draft until APPROVE. After scoped fixes, record the targeted fresh Sol re-review verdict before marking Ready.

<!-- Keep only the review-result line that applies to this PR. -->

## Owner review

- [ ] Functional summary/result matches the intended outcome.
- [ ] Acceptance criteria are satisfied or an explicit blocker is understood.
- [ ] CI/relevant validation is passing.
- [ ] Any required durable decision has been made.
