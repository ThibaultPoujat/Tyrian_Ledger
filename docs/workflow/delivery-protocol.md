# Codex Delivery Protocol

This document contains Git/GitHub delivery rules. The ticket and `AGENTS.md`
define implementation scope and decision gates.

## Branch

Create one branch per ticket:

`ticket/<TICKET_NAME>-<short-kebab-title>`

A bootstrap/pivot branch explicitly authorized by the owner may use an
equivalent descriptive name, but normal M12+ ticket work follows the ticket
pattern.

## Commits

Every normal ticket commit starts with the exact ticket identifier:

`[TKT-Mxx-yy] Short description`

Keep commits logically reviewable. Do not rewrite unrelated history.

## Pull request

Every completed implementation ticket produces a PR before completion.

PR title:

`[TICKET_NAME] Short title`

PR body must include:

- ticket and milestone;
- **functional summary** in plain language (2-6 sentences);
- relevant specification/architecture/ADR references;
- acceptance-criteria status;
- validation commands/checks and results;
- decisions/ADRs, if any;
- VERIFY changes;
- risks/limitations;
- deliberately out-of-scope follow-up work.

Verify the PR exists and report the real URL. Never invent a PR URL. Never merge
the PR; the owner performs the final merge decision.

## Review handoff

After the PR is open, stop implementation. A fresh session reviews it using
`.codex/skills/tyrian-pr-review/SKILL.md`.

If the owner requests fixes, keep them on the same ticket branch and do not add
next-ticket features. Re-run affected validation and make the review status
clear.

## Delivery checklist

- [ ] Acceptance criteria satisfied or explicit blocker recorded.
- [ ] Required functional summary written.
- [ ] Relevant validation performed.
- [ ] Diff reviewed for scope expansion, secrets, data/migration risk, and stale
      contradictory docs.
- [ ] Ticket commits use the ticket prefix (except an explicitly authorized
      bootstrap branch).
- [ ] Branch pushed.
- [ ] PR created and existence verified.
- [ ] PR not merged.
- [ ] VERIFY register current.
- [ ] Fresh review is the next phase, not the next implementation ticket.
