# TKT-M0-02 - Validate GW2 API endpoint and permission matrix

## Milestone
M0

## Goal
Create the authoritative endpoint table used by the application.

## Dependencies
None

## Acceptance criteria
- [ ] List every MVP endpoint, purpose, required permission, batching capability, expected freshness, and cache policy.
- [ ] Mark every uncertain fact VERIFY.
- [ ] Include prices, listings, recipes/search, account tokeninfo, bank/materials, character crafting/recipes, and any other endpoint actually required.
- [ ] No undocumented endpoint is added as a dependency.

## Required tests
- [ ] Review table against current documentation URLs.
- [ ] At least one saved response fixture exists for each endpoint category later required by M2.

## Non-goals
- Implementing the HTTP client.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
