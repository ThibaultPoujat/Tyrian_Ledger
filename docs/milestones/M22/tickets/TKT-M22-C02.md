# TKT-M22-C02 — Rolling Delivery Preparation

GitHub issue: #157

Milestone M22. Risk R2. Owner-authorized Astra planning/workflow change.
Review NORMAL: independent Sol Medium using tyrian-pr-review.

## Outcome

Replace planning after every child merge with a complete dependency map and a
prepared four-ticket queue. One implementation ticket/Goal/PR and owner merge
remain. No runtime engine or screen implementation is part of this preparation.

## Acceptance

- Record #154/#156 merge, green final-head CI and final independent approval.
- Map all remaining P01–P07 work, dependencies, exit evidence, UI references and
  future review expectations; distinguish Planned from conditionally Ready.
- Specify #158–#161 with behavior, boundaries, entry points, examples, validation,
  dependency/readiness checks and explicit model/review assignment.
- Replace stale per-ticket checkpoint rules across active entry docs and #150.
- Provide one reusable next-Ready-ticket Goal and batch evidence handoff.
- Keep #98/INDEX order aligned, #150 open, #96/#97 after package acceptance.
- Remove completed P01B from active gates; automatically hide closed explicit
  gates from generated CURRENT without assigning gates from risk or labels.
- Preserve historical fixtures; test every B1 queue transition and final stop,
  source disagreement and missing authority failures.
- All six image assets unchanged; UI contracts require original/actual comparison.
- Normal CI and independent preparation review pass; publish PR, owner merges.

## Validation / limits

node --test .github/scripts/*.test.mjs; git diff --check; changed Markdown links;
current authority/order reconciliation; required CI. No new API facts or VERIFY
resolution. Future work units remain plans, not fabricated ready tickets.
Commit [TKT-M22-C02], Closes #157, develop, actual milestone 11. Stop after this
preparation; do not implement #158 in the same session.
