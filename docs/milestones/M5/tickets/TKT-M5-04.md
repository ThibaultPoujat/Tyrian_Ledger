# TKT-M5-04 - Implement bounded crafting path search

## Milestone
M5

## Goal
Analyze profitable crafting paths with feasibility constraints and controlled recursion.

## Dependencies
M5-02,M5-03,M3-03

## Acceptance criteria
- [ ] Recipe graph represents output/ingredient relationships.
- [ ] Apply discipline/rating and recipe availability constraints when verified data exists.
- [ ] Search detects cycles and has configurable depth/candidate limits.
- [ ] Memoization prevents repeated subproblem explosion.
- [ ] Analysis reports truncation/unknowns explicitly.
- [ ] Compare purchase cost versus owned-material opportunity cost.

## Required tests
- [ ] Simple recipe.
- [ ] Multi-step recipe.
- [ ] Cycle.
- [ ] Depth cap.
- [ ] Alternative path.
- [ ] Mixed owned/purchased ingredients.

## Non-goals
- Exhaustive unlimited world optimization.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
