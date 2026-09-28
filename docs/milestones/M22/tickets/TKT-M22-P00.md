# TKT-M22-P00 — Approved Product and Visual Baseline

GitHub issue: #148

Milestone: M22. Risk: R1. Review: NORMAL, Astra author and independent Sol Medium
review for this owner-requested preparation. Base: develop.

## Outcome and scope

Make the approved product, six latest prototype images and first bounded coding
ticket durable in Git. Align agent/product/UX/workflow authorities, retain older
references as superseded history, update the PR/review skill and roadmap, and
prevent a Goal from skipping the remaining package gates. No runtime changes.

## Acceptance criteria

- Six original PNGs are committed with screen IDs, hashes and native/target sizes.
- The active visual reference specifies appearance, per-screen use, mockup corrections and actual 1920×1080 screenshot review.
- Product, UX, agent instructions, active tickets and roadmap agree on navigation, session objectives, local confirmation, protections, distribution and quiet behavior.
- Model/review policy supports bounded Luna implementation and preserves explicit stronger gates; Goal prompt invokes the repository review skill.
- First coding ticket #149 contains dependency, scope, regression vectors, commands, model/gate and non-goals. #150 prevents premature release progression.
- Reference integrity checks and historical/current handoff tests pass. Generated CURRENT values reflect actual open/merged state, not proposed completions.
- Independent review and validation are recorded; PR closes #148, matches milestone and awaits owner merge.

## Validation

`node --test .github/scripts/*.test.mjs`; `git diff --check`; active-authority,
relative-link and reference-image inspection; compare #98 order with INDEX.
Normal CI remains required. No new UI has been implemented, so runtime screenshots
are not required for this preparation PR. Do not claim backend fixes or release
acceptance from documentation tests.
