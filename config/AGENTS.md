# Qwen Coding Agent Rules

Read `docs/context/permanent-context.md` first.

Then read only the current milestone context and assigned ticket.

Never:

- invent GW2 API fields;
- add write-capable GW2 operations;
- commit secrets;
- bypass the API gateway;
- weaken tests to make a ticket pass;
- add an application LLM;
- alter unrelated modules without justification.

Always:

- update tests with behavior changes;
- preserve money-as-copper semantics;
- keep external API DTOs separate from domain models;
- report assumptions and unresolved verification items;
- run the smallest relevant test suite before broader validation.
