You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M0.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M0-01.md

Then read only the MTPLX/model documentation explicitly required by the ticket.

## Mission

Complete TKT-M0-01 only.

Determine whether the user-provided Qwen3.8-27B-GGUF can be used directly, must be
converted, or should be replaced by an MTPLX-compatible Qwen3.8 27B artifact.

Acceptance-critical work:
- inspect current MTPLX compatibility behavior;
- run the smallest safe model inspection/load command available;
- document the raw GGUF result;
- document the chosen compatible artifact and provenance when applicable;
- keep MTPLX completely outside application architecture.

## Non-goals

- application code changes;
- application LLM integration;
- training a model;
- redesigning the architecture.

## Hard rules

- Never invent model/runtime behavior.
- Record unresolved external facts as VERIFY.
- Do not claim a live runtime smoke occurred unless it actually did.
- Preserve the application/runtime separation from MTPLX.
- Minimize unrelated changes.

## Execution

1. Inspect the ticket and relevant MTPLX/model evidence.
2. Make a maximum five-step plan.
3. Perform the smallest safe inspection.
4. Record the evidence and decision in the ticket or required ADR/document.
5. Validate the acceptance criteria and inspect the diff.
6. Stop.

Do not repeatedly summarize the same evidence. Do not reread unchanged files more than
twice. Do not retry the same failed command more than twice; report the exact blocker.

## Validation

Validate the documented command/result and ensure every unresolved external claim is
marked VERIFY. Do not invent application tests for this documentation/runtime-selection
ticket.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
