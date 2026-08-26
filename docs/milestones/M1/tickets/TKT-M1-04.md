# TKT-M1-04 - Add local-only runtime and baseline security middleware

## Milestone
M1

## Goal
Make loopback-only operation and safe HTTP defaults explicit without introducing public/LAN exposure or inventing future business endpoints.

## Dependencies
M1-01,M1-02

## Acceptance criteria
- [ ] Default development server binds to loopback (`127.0.0.1`) unless an explicit developer override is supplied.
- [ ] Add a documented minimal set of response-security headers appropriate to the current local HTTP application; do not enable HSTS unless HTTPS is actually configured.
- [ ] Establish the API-boundary input-validation mechanism to be used by future request DTOs. For the current skeleton, do not create a fake business endpoint solely to demonstrate validation; use the smallest reusable validation/test surface justified by the existing HTTP code.
- [ ] Document how to intentionally change the binding and why LAN/public exposure is not recommended.
- [ ] Preserve the local-only default across development and test startup paths.

## Required tests
- [ ] Configuration/startup test asserts loopback default.
- [ ] Security-header smoke test covers the selected headers.
- [ ] Validation mechanism test covers at least one representative request/input model where an input surface exists; otherwise validate the reusable validator with a deterministic unit test.

## Non-goals
- LAN/public hosting support.
- HSTS without HTTPS.
- Business API endpoint implementation.
- Authentication/authorization implementation.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/security/security.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
