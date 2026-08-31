# TKT-M9-00 - Define beginner fast-flip MVP

## Goal

Create the durable planning and implementation contracts for M9 without
changing application behavior.

## Dependencies

- M8-04 local release handoff.

## References

- [M9 milestone plan](../../M9.md)
- [M9 milestone context](../../../context/milestone-context-M9.md)
- [Permanent context](../../../context/permanent-context.md)
- [VERIFY register](../../../verification/VERIFY-REGISTER.md)
- [Delivery protocol](../../../workflow/delivery-protocol.md)

## Acceptance criteria

- M9 has a complete milestone plan for the approved beginner fast-flip MVP.
- A concise M9 context enables a fresh task to work from one ticket at a time.
- Tickets TKT-M9-01 through TKT-M9-05 state ordered dependencies, scope,
  acceptance criteria, required tests, explicit non-goals, and references.
- The milestone index links M9 and every M9 ticket.
- VERIFY-007 reflects M9's required public item metadata work.
- VERIFY-013 records the unresolved Guild Wars 2 fee rounding/minimum
  contract required by the financial model.
- No application code, production data, migration, existing milestone history,
  or runtime configuration changes.

## Required validation

- Confirm all new Markdown links and referenced ticket paths exist.
- Run git diff --check.
- Inspect the documentation-only diff for scope and secrets.

## Non-goals

- Implementing any M9 product behavior.
- Resolving external API or fee-contract uncertainty.
- Altering prior milestone plans or tickets.

## Delivery

- Branch: ticket/TKT-M9-00-beginner-mvp-planning
- Commit: [TKT-M9-00] Define beginner fast-flip MVP
- PR title: [TKT-M9-00] Define beginner fast-flip MVP
