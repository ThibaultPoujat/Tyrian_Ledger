# TKT-M22-C03 — B2 Account Evidence Preparation

GitHub issue: #167

Milestone M22. Risk R2. Owner-authorized Astra planning preparation.
Review NORMAL: independent Sol Medium using tyrian-pr-review.

## Outcome

Prepare four bounded backend contracts after B1, preserving one ticket/Goal/PR,
independent review and owner merge. This PR changes delivery contracts and handoff
regressions; it implements no runtime account, financial or UI behavior.

## Acceptance

- Verify #158–#161 merges, final-head CI, recorded independent reviews and the
  evidence-only changes after reviewed implementation heads; retain exact links.
- Inspect original 01–04 PNGs and actual screenshots. Record the owner's
  “Changes needed” response without claiming usability acceptance or inventing
  requested changes. Keep the six originals unchanged and UI feedback open.
- Split B2 into #168 collection, #169 protections/actors, #170 epochs and #171
  protected projection, with entry evidence, bounded behavior, non-goals,
  representative failure vectors and required validation.
- Assign Luna High implementation explicitly; NORMAL Sol Medium for #168,
  NORMAL Sol High for #169/#171, fresh Sol XHigh gate for #170.
- Align INDEX/#98, active context, model guide, reusable Goal and #150. Preserve
  historical B1 tests and add B2 progression/fail-closed authority regressions.
- Preserve the complete remaining map. Keep Windows feasibility planned at the
  next checkpoint; it is not an executable promise without Windows evidence.
- Record new external coverage/coherence uncertainties in VERIFY; do not equate
  documentation or synthetic tests with live account completeness.
- Publish a reviewed preparation PR with green CI, actual milestone 11,
  Closes #167; owner merges. Do not implement #168 in the same session.

## Validation

node --test .github/scripts/*.test.mjs; git diff --check; changed Markdown links;
authoritative queue/model reconciliation; required CI. Runtime suites run in CI,
but no new runtime financial behavior is claimed. Commit [TKT-M22-C03].
