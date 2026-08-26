# Codex Delivery Protocol

This document contains Git/GitHub delivery rules only. The ticket and
`AGENTS.md` define execution rules.

## Branch

Create one branch per ticket:

`ticket/<TICKET_NAME>-<short-kebab-title>`

## Commits

Every commit belonging to the ticket MUST start with the exact ticket identifier:

`[TKT-Mx-yy] Short description`

Keep commits logically reviewable. Do not rewrite unrelated history.

## Pull request

Every completed ticket MUST produce a GitHub pull request before it can be declared
complete.

PR title:

`[TICKET_NAME] Short title`

The PR body must include:

- ticket and milestone;
- exact specification/architecture/ADR/security/testing/UX references implemented or validated;
- summary;
- acceptance-criteria status;
- validation commands/tests and results;
- decisions/ADRs, if any;
- VERIFY items;
- risks and limitations;
- follow-up work.

Codex MUST verify that the PR actually exists and report its URL.

Never invent a PR URL.

Never merge the PR. The owner performs the final functional review and merge.

If branch push, GitHub authentication, CLI/remote access, or permissions prevent PR
creation, stop at the delivery gate and report the exact blocker. Do not claim the
ticket is complete.

## Delivery checklist

Before completion:

- [ ] Acceptance criteria satisfied or explicit blocker recorded.
- [ ] Relevant validation performed.
- [ ] Diff reviewed for scope expansion and secrets.
- [ ] All ticket commits use the ticket prefix.
- [ ] Branch pushed.
- [ ] PR created with the required title/body.
- [ ] PR existence verified and URL reported.
- [ ] PR not merged.
- [ ] VERIFY register is current.
