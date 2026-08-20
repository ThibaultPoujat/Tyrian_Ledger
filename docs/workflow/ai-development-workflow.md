# Qwen + MTPLX Development Workflow

## Role of Qwen

Qwen is the development agent. It is not a product decision-maker and is not the application's business-logic authority.

## Context hierarchy

Load the minimum context needed:

1. `docs/context/permanent-context.md`
2. `docs/context/milestone-context-Mx.md`
3. exactly one ticket from `docs/tickets/`
4. the ticket prompt from `docs/prompts/`
5. only the relevant source files.

Do not paste the entire specification into every request.

## Ticket loop

For every ticket:

1. Read the ticket and acceptance criteria.
2. Inspect current repository state.
3. Identify contradictions or missing dependencies.
4. State implementation plan briefly.
5. Implement only the ticket scope.
6. Add/update tests before declaring success.
7. Run formatter/analyzers/tests.
8. Inspect changed files for secret leakage and accidental scope expansion.
9. Report files changed, tests run, known limitations, and any specification issue.

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

## Ticket delivery gate

A ticket is not complete until all of the following are true:

1. Acceptance criteria are satisfied or an explicit blocker is recorded.
2. Relevant tests/validation have been run.
3. The diff has been reviewed for scope expansion and secrets.
4. All ticket commits use the required `[TICKET_NAME]` prefix.
5. The branch is pushed to the configured GitHub remote.
6. A PR exists with the required title and specification references.
7. Qwen reports the PR URL and does not merge it.

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
