# TKT-M0-04 - Validate legal/security scope and read-only boundary

## Milestone
M0

## Goal
Produce the release gate for terms, API-key handling, local security, and the strict read-only boundary.

## Dependencies
None

## Acceptance criteria
- [ ] Document current API terms references.
- [ ] Document personal-use GDPR/French security assumptions and what changes for public deployment.
- [ ] Define minimum API-key permissions by feature.
- [ ] Define explicit prohibited operations and a testable architecture rule preventing generic writes.

## Required tests
- [ ] Security checklist exists.
- [ ] Read-only regression test idea is recorded.

## Non-goals
- Legal advice beyond documenting requirements and assumptions.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Agent prompt
See the dedicated prompt file: `docs/prompts/{code}-prompt.md`.
