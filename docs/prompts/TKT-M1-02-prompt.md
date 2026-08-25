You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M1.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M1-02.md

Then read only these specialized documents because this ticket explicitly depends on them:
- docs/adr/ADR-006-secrets.md
- docs/security/security.md
- docs/architecture/architecture.md
- docs/testing/testing-strategy.md

## Mission

Complete TKT-M1-02 only.

Create the local secret/configuration boundary required by ADR-006. The implementation must
keep credential values out of source control, browser-visible data, logs, exceptions, and
persistent application data.

## Required outcome

- application-facing secret abstraction (for example `ISecretStore`);
- development/test environment-variable provider;
- clear configuration error when the credential is required but unavailable;
- dependency-injection wiring at the composition root;
- focused tests for missing secret, environment lookup, log/exception redaction, and web-boundary disclosure;
- no plaintext file secret store;
- no real GW2 credential anywhere in tests or fixtures.

The accepted architecture remains the one in ADR-006: an OS-backed provider is the intended
persistent local mechanism; the environment-variable provider is only the documented development/
test fallback. Do not invent a new secret-storage architecture or silently replace ADR-006.

## Important implementation boundary

Keep secret retrieval outside Domain and Analytics.
Controllers/web DTOs may consume only a non-secret configuration state or a success/failure result,
never the credential value.

If the current skeleton has no HTTP path that can exercise the web-boundary requirement, add only
the smallest non-secret configuration/status surface needed to test that the credential value is
never serialized. Do not create a generic secret/configuration dump endpoint.

## Hard rules

- Never hard-code or persist credential/token values in source, browser storage, logs, fixtures,
  prompts, tests, or documentation.
- Never use a real GW2 API key in any test.
- Do not add cloud secret management.
- Do not add GW2 API calls.
- Do not add gameplay or Trading Post automation.
- Do not modify unrelated architecture or ADRs.
- Do not add the application LLM.

## Execution

1. Inspect the repository, M1-01 skeleton, ADR-006, security rules, and current configuration.
2. Make a maximum five-step implementation plan.
3. Implement the smallest coherent abstraction/provider/wiring needed by the ticket.
4. Add the required focused tests using synthetic secret values only.
5. Run the narrow tests first, then broader backend validation; inspect the diff for secret leakage.
6. Stop.

Do not repeatedly reread unchanged files.
Do not retry the same failed operation more than twice.
If the secure design is genuinely blocked after two attempts, report BLOCKED with the exact reason and stop.

## Validation

At minimum validate:
- missing credential produces a stable non-secret configuration error;
- environment provider resolves a synthetic credential;
- logs/exceptions do not contain the synthetic credential;
- no HTTP response contains the synthetic credential;
- the application still builds with warnings treated as errors.

Never weaken or delete a test merely to make it pass.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only:
- files changed;
- acceptance-criteria status;
- validation commands/results;
- VERIFY items added/updated;
- known limitations or blockers;
- verified PR URL when the delivery gate is complete.
