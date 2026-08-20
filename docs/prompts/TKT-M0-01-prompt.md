You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M0.md
- docs/tickets/TKT-M0-01.md

Your job is to implement ONLY this ticket. Do not redesign the application or add an LLM feature.

Rules:
- Never invent GW2 API fields, permissions, quotas, or behavior. Mark uncertain facts VERIFY.
- Preserve the read-only boundary. Do not add gameplay or Trading Post automation.
- Do not place API keys in source code, browser storage, logs, fixtures, prompts, or tests.
- Keep money calculations in integer copper.
- Keep external API DTOs separate from domain models.
- Route GW2 requests through the single gateway defined by the architecture.
- Add or update tests for every behavior change. Never weaken or delete a test just to make it pass.
- Minimize unrelated file changes.

Execution protocol:
1. Inspect the current repository and relevant existing code.
2. Restate the ticket acceptance criteria in implementation terms.
3. Identify any real contradiction or missing dependency before coding. If one exists, stop and explain it rather than inventing a solution.
4. Implement the smallest coherent change satisfying the ticket.
5. Add/update unit, integration, or browser tests as appropriate.
6. Run the narrow test set first, then the relevant broader test set.
7. Check formatting/analyzers/build.
8. Review the diff for accidental scope expansion and secret leakage.
9. Finish with: files changed, tests run, results, known limitations, VERIFY items, and suggested next ticket.

Ticket-specific objective:
Determine whether the user-provided Qwen3.8-27B-GGUF can be used directly, must be converted, or should be replaced by an MTPLX-compatible Qwen3.8 27B artifact.

Ticket-specific acceptance criteria:
- Inspect current MTPLX documentation and model compatibility behavior.
- Run the smallest safe model inspection/load command available on the Mac.
- Document whether raw GGUF loads directly.
- Document the chosen MTPLX-compatible artifact and provenance if conversion/catalog artifact is required.
- Do not modify application code to couple it to MTPLX.

Ticket-specific non-goals:
- Changing the application architecture.
- Training a model.


Delivery protocol (mandatory for every ticket):
- Create/use a dedicated branch named `ticket/<TKT-M0-01>-<short-kebab-title>`.
- Every commit for this ticket MUST start with `[TKT-M0-01]`.
- Before declaring the ticket complete, push the branch and create a GitHub pull request titled `[TKT-M0-01] Short title`.
- The PR body MUST identify the ticket and milestone and list the exact specification/architecture/ADR/testing/security/UX references implemented or validated, plus summary, acceptance-criteria status, validation, decisions, VERIFY items, risks/limitations, and follow-up.
- Verify that the PR actually exists and report its URL. Never invent a PR URL.
- Do not merge the PR. Human review and merge are required.
- If GitHub CLI/authentication/permissions/remote access prevent PR creation, stop at the delivery gate and report the blocker; do not claim the ticket is complete.
