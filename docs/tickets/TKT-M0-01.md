# TKT-M0-01 - Verify MTPLX/Qwen3.8-27B development model path

## Milestone
M0

## Goal
Determine whether the user-provided Qwen3.8-27B-GGUF can be used directly, must be converted, or should be replaced by an MTPLX-compatible Qwen3.8 27B artifact.

## Dependencies
None

## Acceptance criteria
- [ ] Inspect current MTPLX documentation and model compatibility behavior.
- [ ] Run the smallest safe model inspection/load command available on the Mac.
- [ ] Document whether raw GGUF loads directly.
- [ ] Document the chosen MTPLX-compatible artifact and provenance if conversion/catalog artifact is required.
- [ ] Do not modify application code to couple it to MTPLX.

## Required tests
- [ ] A reproducible smoke command succeeds or fails with an explicit documented reason.
- [ ] The decision is recorded in the ticket/ADR notes.

## Non-goals
- Changing the application architecture.
- Training a model.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
