# TKT-M0-04 - Validate legal/security scope and read-only boundary

## Milestone
M0

## Goal
Validate and consolidate the project's security, privacy, API-key, and strict read-only
requirements into a release-gate document without introducing new application
architecture.

## Dependencies
- M0-02 endpoint/permission matrix
- M0-03 rate-limit policy
- ADR-006 secret handling
- ADR-007 read-only boundary

## Acceptance criteria
- [ ] Document the authoritative current GW2 API terms/documentation references used by the project.
- [ ] Document personal/local-use French/EU privacy and security assumptions and clearly distinguish them from controls required for public deployment.
- [ ] Define minimum API-key permissions by feature using verified API documentation; unresolved mappings remain VERIFY.
- [ ] Document explicit prohibited operations and restate the architectural rule that feature code cannot access a generic authenticated/write-capable GW2 operation.
- [ ] Identify future automated enforcement requirements without falsely claiming that runtime enforcement already exists.
- [ ] Add or update material VERIFY items with stable IDs and evidence references.

## Required validation
- [ ] Security/release checklist exists and is internally consistent.
- [ ] Every external API/legal claim is either sourced from an authoritative project reference or marked VERIFY.
- [ ] The read-only enforcement requirement is explicitly documented.
- [ ] No application behavior is falsely claimed to be implemented.
- [ ] Final diff contains no credential/token values.

## Non-goals
- Legal advice.
- Implementing the GW2 API client or gateway.
- Implementing credential storage.
- Implementing runtime read-only enforcement.
- Creating executable read-only regression tests before the gateway exists.
- Adding an application LLM.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/architecture/gw2-endpoint-matrix.md`
- `docs/security/security.md`
- `docs/testing/testing-strategy.md`
- `docs/adr/ADR-006-secrets.md`
- `docs/adr/ADR-007-read-only-boundary.md`
- `docs/verification/VERIFY-REGISTER.md`
- `docs/workflow/agent-execution-rules.md`
- `docs/workflow/delivery-protocol.md`

## Implementation note

This is primarily a documentation and verification ticket. If a required external fact
cannot be verified, record it as VERIFY and continue with all work that does not depend
on the unresolved fact. Stop only if the missing information makes the requested work
technically impossible or unsafe.

## Agent prompt
See `docs/prompts/TKT-M0-04-prompt.md`.
