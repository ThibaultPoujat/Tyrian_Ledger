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

## Commit guidance

One logical ticket per commit is preferred. Suggested message:

`TKT-M3-02: add fee-aware flip calculator`

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
