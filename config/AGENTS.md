# Tyrian Ledger — Qwen Coding Agent Rules

Read `docs/context/permanent-context.md` first.

Then read only the current milestone context, assigned ticket, and that ticket's execution prompt.

For ticket execution, also follow `docs/workflow/ai-development-workflow.md` and `docs/context/qwen-git-context.md`.

Before starting any ticket, review `docs/verification/VERIFY-REGISTER.md`.

Never:

- invent GW2 API fields;
- add write-capable GW2 operations;
- commit secrets;
- bypass the API gateway;
- weaken tests to make a ticket pass;
- add an application LLM;
- alter unrelated modules without justification.
- claim a ticket is complete without creating its required pull request.
- merge a pull request without explicit human approval.

Always:

- update tests with behavior changes;
- preserve money-as-copper semantics;
- keep external API DTOs separate from domain models;
- report assumptions and unresolved verification items;
- maintain `docs/verification/VERIFY-REGISTER.md`: add new VERIFY items discovered during the assigned ticket, update affected existing items, and mark items RESOLVED only when the ticket contains sufficient supporting evidence;
- run the smallest relevant test suite before broader validation.


## Mandatory Git and pull-request delivery

For every ticket, Qwen MUST work on a dedicated branch named `ticket/<TICKET_NAME>-<short-kebab-title>`, unless the repository workflow explicitly requires another branch name.

Every commit belonging to a ticket MUST start with the exact ticket identifier in square brackets:

`[TKT-M0-01] short description`

Every ticket MUST result in a GitHub pull request before the ticket is declared complete. The pull-request title MUST start with the exact ticket identifier in square brackets, followed by a short title:

`[TKT-M0-01] Short title`

The pull-request body MUST state:

- ticket and milestone;
- specification/architecture references the work implements or validates;
- summary of changes;
- acceptance-criteria status;
- tests/validation performed and results;
- decisions and ADRs, if any;
- VERIFY items, limitations, and known risks;
- any follow-up work.

Qwen MUST NOT merge its own pull request. Human review and merge remain mandatory.

Qwen MUST verify that the pull request was actually created and report its URL. If GitHub CLI/authentication or repository permissions are unavailable, Qwen MUST stop before claiming completion and report the exact blocker. It MUST NOT fabricate a PR URL or claim that a PR exists when it does not.
