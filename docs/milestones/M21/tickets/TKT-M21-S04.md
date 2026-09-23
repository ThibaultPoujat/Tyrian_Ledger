# TKT-M21-S04 - Remove Superseded UI, Docs, and Code after Signals MVP

GitHub issue: #132

## Milestone

M21 - Signals and Crafting Intelligence

## Goal

After the Signals MVP, displayed as `Mes Signaux`, proves which existing capabilities are still needed, remove or archive superseded product surfaces and documentation without deleting useful backend financial/evidence engines.

## Requirements

- Audit React routes/pages/components made redundant by the Signals surface and the new primary navigation.
- Remove obsolete `What Should I Do?` naming/copy and other stale user-facing terminology where it no longer represents the product.
- Remove or consolidate redundant Dashboard/Scanner/Personal Learning/Inventory presentation code only when required capabilities are already exposed through Signals, the settings destination displayed as `Réglages`, diagnostics, or another retained surface.
- Keep deterministic backend analytics, accounting, history, scoring, sizing, personal evidence and gateway code that remains an input to current/future Signals.
- Keep useful historical ADR/ticket records for traceability; mark them historical/superseded rather than rewriting history.
- Search README/docs/tests/E2E/scripts/screenshots for stale primary-navigation/product assumptions and update/delete them as appropriate.
- Do not restore legacy M10-M11 static/public runtime concepts.
- All retained user-facing UI copy touched by this ticket remains French; repository/internal terminology remains English.

## Acceptance criteria

- [ ] No duplicate primary user journey remains solely because of the pre-Signals UI structure.
- [ ] No actively read source-of-truth document tells agents to treat obsolete pages as primary destinations.
- [ ] Removed UI/code has no surviving runtime/test dependency, or the dependency is deliberately migrated first.
- [ ] Historical architectural/ticket evidence remains available where useful.
- [ ] Full relevant regression/E2E suites remain green.

## Dependencies

#131.

## Recommended Codex configuration

Risk class: **R2 (cleanup/refactor with cross-layer regression risk)**.

Review path: **NORMAL** with Terra High by default and an independent same-run review/check.

## Validation

- Search for stale navigation/product terminology and document intentional historical exceptions.
- Run relevant .NET, frontend and Playwright regression suites.
- Verify deleted routes/components have no runtime/test imports.
- Verify retained diagnostics are not primary navigation.

## Non-goals

- Rewriting working financial engines for style reasons.
- Building Active/Passive plan orchestration.
- Crafting implementation.
