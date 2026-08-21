You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M0.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M0-SETUP-01.md

Your job is to implement ONLY this ticket. Do not redesign the application or add an LLM feature.

Rules:
- This is a documentation/workflow maintenance ticket. Do not create or modify application code.
- Never invent GW2 API fields, permissions, quotas, or behavior. Mark uncertain facts VERIFY.
- Do not modify unrelated project architecture or specifications.
- Do not create an ADR solely for introducing the VERIFY register.
- Do not resolve a VERIFY item without recording supporting evidence in the ticket or another authoritative document.
- Minimize unrelated file changes.

## VERIFY register requirements

Before implementation:

1. Read `docs/verification/VERIFY-REGISTER.md`.
2. Identify existing VERIFY items relevant to this ticket.
3. Do not assume unresolved items are true.
4. During implementation, record every newly discovered external-contract, security, legal, architectural, data-availability, or other material uncertainty as a VERIFY item.
5. Update `docs/verification/VERIFY-REGISTER.md` before completing the ticket.
6. Mark a VERIFY item `RESOLVED` only when the ticket contains sufficient supporting evidence.
7. Do not delete resolved VERIFY items.
8. Reference relevant VERIFY IDs in the ticket's final report.

The ticket is not complete if a material VERIFY item discovered by the ticket has not been recorded in the VERIFY register.

Execution protocol:
1. Inspect the current repository and relevant documentation.
2. Restate the ticket acceptance criteria in implementation terms.
3. Identify any real contradiction or missing dependency before making changes. If one exists, stop and explain it rather than inventing a solution.
4. Apply the smallest coherent documentation change satisfying the ticket.
5. Check Markdown formatting and cross-document consistency.
6. Review the diff for accidental scope expansion.
7. Finish with: files changed, results, known limitations, VERIFY items, and suggested next ticket.

Ticket-specific objective:
Establish `docs/verification/VERIFY-REGISTER.md` and integrate the VERIFY workflow into the permanent Qwen development process (agent rules, permanent context, ticket lifecycle, prompt generation, and PR review), importing the unresolved TKT-M0-01 VERIFY items.

Ticket-specific acceptance criteria:
- Register exists with stable IDs, statuses, owner ticket, and dates.
- The three unresolved TKT-M0-01 VERIFY items are imported as OPEN items.
- `config/AGENTS.md` requires pre-ticket register review and register maintenance.
- `permanent-context.md` designates the register as the authoritative project-level index.
- The workflow includes Before/During/Before completion VERIFY phases.
- The prompt-generation workflow mandates the VERIFY register requirements section.
- The PR review workflow includes the verification-register checklist.
- Register/ticket distinction documented: register is an index, tickets hold evidence.
- No application code, architecture, or specification changes.

Ticket-specific non-goals:
- Changing the application architecture.
- Retroactively editing existing ticket prompts.
- Creating an ADR, scripts, or a VERIFY API.


Delivery protocol (mandatory for every ticket):
- Create/use a dedicated branch named `ticket/TKT-M0-SETUP-01-<short-kebab-title>`.
- Every commit for this ticket MUST start with `[TKT-M0-SETUP-01]`.
- Before declaring the ticket complete, push the branch and create a GitHub pull request titled `[TKT-M0-SETUP-01] Short title`.
- The PR body MUST identify the ticket and milestone and list the exact specification/architecture/ADR/testing/security/UX references implemented or validated, plus summary, acceptance-criteria status, validation, decisions, VERIFY items, risks/limitations, and follow-up.
- Verify that the PR actually exists and report its URL. Never invent a PR URL.
- Do not merge the PR. Human review and merge are required.
- If GitHub CLI/authentication/permissions/remote access prevent PR creation, stop at the delivery gate and report the blocker; do not claim the ticket is complete.
