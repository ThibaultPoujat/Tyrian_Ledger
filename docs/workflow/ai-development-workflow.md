# Qwen + MTPLX Development Workflow

## Role of Qwen

Qwen is the development agent. It is not a product decision-maker and is not the application's business-logic authority.

## Context hierarchy

Load the minimum context needed:

1. `docs/context/permanent-context.md`
2. `docs/context/milestone-context-Mx.md`
3. `docs/verification/VERIFY-REGISTER.md`
4. exactly one ticket from `docs/tickets/`
5. the ticket prompt from `docs/prompts/`
6. only the relevant source files.

Do not paste the entire specification into every request.

## Ticket loop

### Before implementation

1. Read permanent context.
2. Read current milestone context.
3. Read the VERIFY register.
4. Read the assigned ticket and its acceptance criteria.
5. Read the assigned ticket prompt.
6. Inspect current repository state.
7. Identify relevant existing VERIFY items.
8. Confirm unresolved VERIFY items are not silently treated as facts.
9. Identify contradictions or missing dependencies.
10. State the implementation plan briefly.

### During implementation

1. Implement only the ticket scope.
2. Record material uncertainties.
3. Add new VERIFY items to the register.
4. Update existing VERIFY items when new evidence changes them.
5. Keep detailed evidence in the ticket or the appropriate authoritative document.
6. Add/update tests before declaring success.
7. Run formatter/analyzers/tests.
8. Inspect changed files for secret leakage and accidental scope expansion.

### Before completion

1. Review the VERIFY register.
2. Confirm every new material VERIFY item is registered.
3. Confirm resolved items have supporting evidence.
4. Reference relevant VERIFY IDs in the ticket.
5. Do not silently delete unresolved items.
6. Report files changed, tests run, known limitations, and any specification issue.

## When Qwen should stop

Qwen must stop and ask for a human decision when:

- a current API fact contradicts the specification;
- a ticket requires a write-capable external operation;
- an architectural decision must change;
- a secret is required but no secure mechanism is configured;
- tests and requirements appear mutually inconsistent;
- a dependency requires accepting an unacceptable license or security risk.

## Prompt style

Prompts should be imperative, scoped, test-oriented, and explicit about non-goals. Avoid “build the app” requests.

Every ticket prompt generated for this project MUST include the "VERIFY register requirements" section below so future ticket prompts carry the VERIFY workflow. Existing ticket prompts are historical records and MUST NOT be retroactively modified; if an existing ticket is reopened, update its prompt with this section before execution.

### VERIFY register requirements

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

## Git branch, commit, and pull-request protocol

Each ticket is an independently reviewable unit. Qwen MUST create a dedicated branch before implementation:

`ticket/<TICKET_NAME>-<short-kebab-title>`

All commits for that ticket MUST start with the exact ticket identifier in square brackets:

`[TKT-M3-02] Add fee-aware flip calculator`

One logical ticket per commit is preferred; multiple commits are allowed when they improve reviewability, but every commit must use the ticket prefix.

Every completed ticket MUST have a GitHub pull request. The PR title MUST use:

`[TICKET_NAME] Short title`

Example:

`[TKT-M3-02] Add fee-aware flip calculator`

The PR body MUST include:

- **Ticket:** exact ticket identifier and link/path to the ticket.
- **Milestone:** milestone identifier.
- **Specification references:** exact specification, architecture, ADR, security, testing, and/or UX documents that this PR implements or validates.
- **Summary:** concise description of what changed.
- **Acceptance criteria:** checklist showing each criterion as complete, incomplete, or not applicable with rationale.
- **Validation:** commands/tests run and their results.
- **Decisions:** ADRs or durable decisions made, if any.
- **VERIFY items:** unresolved external facts or assumptions.
- **Risks/limitations:** relevant known limitations.
- **Follow-up:** remaining work, if any.

Qwen MUST actually create the PR and report its URL. It MUST NOT invent a URL or claim that a PR exists without verifying it. If the GitHub CLI, authentication, remote, or permissions needed to create the PR are unavailable, Qwen MUST stop at the delivery gate and report the blocker rather than declaring the ticket complete.

Qwen MUST NOT merge the PR. Human review and merge are mandatory.

## Verification register review

The pull-request review includes this verification-register checklist:

- [ ] Reviewed `docs/verification/VERIFY-REGISTER.md`
- [ ] Added all new VERIFY items discovered by this ticket
- [ ] Updated affected existing VERIFY items
- [ ] No VERIFY item was marked RESOLVED without supporting evidence
- [ ] Relevant VERIFY IDs are referenced in the ticket

The register is an index; the ticket is the primary evidence record. Detailed investigation belongs in `docs/tickets/`, not in the register.

## Ticket delivery gate

A ticket is not complete until all of the following are true:

1. Acceptance criteria are satisfied or an explicit blocker is recorded.
2. Relevant tests/validation have been run.
3. The diff has been reviewed for scope expansion and secrets.
4. All ticket commits use the required `[TICKET_NAME]` prefix.
5. The branch is pushed to the configured GitHub remote.
6. A PR exists with the required title and specification references.
7. Qwen reports the PR URL and does not merge it.
8. The VERIFY register is current: new material items are recorded, resolved items have supporting evidence, relevant IDs are referenced in the ticket, and no unresolved item was silently deleted.

## ADRs

An ADR is an Architecture Decision Record: a short document that records an important architectural decision, its context, the chosen option, alternatives, and consequences.

Create/update an ADR when a decision is durable and cross-cutting.

## Review rules

Human review is required for:

- changes to API permissions;
- secrets/authentication;
- API request policy;
- financial formulas;
- persistence schema decisions;
- security behavior;
- additions to project scope.
