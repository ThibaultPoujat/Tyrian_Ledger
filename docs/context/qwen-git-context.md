# Qwen Git and PR Context

This is a lightweight delivery context. Read it for every ticket.

## Branch

Create one branch per ticket:

`ticket/<TICKET_NAME>-<short-kebab-title>`

## Commits

Every commit for the ticket MUST start with:

`[TICKET_NAME]`

Example: `[TKT-M0-01] Record MTPLX model compatibility decision`

## Pull requests

Every ticket MUST produce one GitHub pull request before it can be declared complete.

PR title format:

`[TICKET_NAME] Short title`

The PR body must identify the ticket, milestone, exact specification/architecture/ADR/testing/security/UX references, summary, acceptance criteria, validation, decisions, VERIFY items, risks/limitations, and follow-up work.

Qwen must verify the PR exists and report its URL. Never invent a PR URL. Never merge the PR; human review and merge are required.

If GitHub remote access, authentication, permissions, or the required CLI are unavailable, stop at the delivery gate and report the blocker instead of declaring the ticket complete.
