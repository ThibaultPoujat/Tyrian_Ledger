# TKT-M1-02 - Add local configuration and secret-store abstraction

## Milestone
M1

## Goal
Create the local credential/configuration boundary required by the architecture without ever persisting an API key in source control, browser storage, logs, or ordinary application configuration.

## Dependencies
M1-01

## Acceptance criteria
- [ ] Define and implement an `ISecretStore`/equivalent application-facing abstraction for retrieving the GW2 API credential without exposing its value to feature code or the web UI.
- [ ] Support a documented local-development environment-variable override (for example, a single `TYRIAN_LEDGER_*` variable whose exact name is documented by the implementation).
- [ ] Preserve ADR-006: production/local persistent storage must use an OS-backed secret mechanism; environment-variable retrieval is only a development/test fallback.
- [ ] Ensure credential values are never written to logs, exceptions, browser responses, DTOs, or persisted application data.
- [ ] Return a stable, non-secret configuration error when the credential is required but unavailable.
- [ ] Keep secret retrieval outside Domain and Analytics layers.

## Required tests
- [ ] Missing-secret test: absent credential produces the stable configuration error without leaking a value.
- [ ] Environment-provider test: a synthetic credential can be resolved for local development/test execution.
- [ ] Log-redaction test: a synthetic credential never appears in captured logs/exceptions.
- [ ] Web-boundary test: a synthetic credential is not present in any HTTP response/serialized DTO; if no existing endpoint exercises configuration yet, add only the smallest non-secret configuration/status surface needed to prove this.
- [ ] Tests never use a real GW2 credential.

## Non-goals
- Cloud secret management.
- Frontend localStorage/sessionStorage for credentials.
- GW2 API client implementation.
- Trading Post or gameplay automation.
- Introducing a generic configuration endpoint that exposes secret values.

## Implementation constraints
- Follow ADR-006 and `docs/security/security.md`.
- Prefer an explicit provider abstraction with dependency injection over direct reads from environment variables inside controllers or business logic.
- Do not add a plaintext file-based secret store as a fallback.
- Keep the secret value out of domain models and application DTOs.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/adr/ADR-006-secrets.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.
