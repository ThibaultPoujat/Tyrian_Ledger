# Security and Data Protection Baseline

## Scope

The initial application is local and personal. This is intentionally not a multi-user web service.

For a genuinely personal/household activity, GDPR Article 2(2)(c) can exclude the activity from GDPR scope. That conclusion is context-dependent and must be reassessed if the application is commercialized, shared, hosted, or used for other persons' data.

References:
- https://eur-lex.europa.eu/eli/reg/2016/679/oj
- https://www.cnil.fr/fr/securite-api-interfaces-de-programmation-applicative
- https://www.cnil.fr/fr/passer-laction/garantir-la-securite-des-donnees
- https://www.ssi.gouv.fr/entreprise/bonnes-pratiques/

## Mandatory/conditional requirements

1. Comply with current Guild Wars 2 API and website/content terms.
2. If the scope becomes non-personal, apply GDPR/French data-protection obligations appropriate to that deployment.
3. Protect API credentials against unauthorized access.
4. Apply security controls proportionate to risk.
5. Reassess legal/security scope before public or hosted deployment.

## Best practices required by this project

- API key stored outside source code;
- no secret in browser JavaScript;
- loopback-only server by default;
- redacted logs;
- minimal local data retention;
- explicit clear-data action;
- dependency updates;
- test fixtures free of real API keys;
- no analytics/telemetry sent remotely without explicit future decision;
- secure default headers where compatible with local development;
- input validation on all locally exposed HTTP endpoints;
- no unsafe HTML rendering of API-controlled names or text.

## API token specifics

The current GW2 tokeninfo documentation warns that the key name is not escaped and can contain HTML/JavaScript. Render it as text only.

Permissions required by the application must be declared per feature. The key validator should show missing permissions and disable corresponding functionality instead of repeatedly attempting unauthorized calls.

## Data minimization

Prefer storing:

- opaque account identifier or local profile ID;
- selected account-derived facts needed for current analysis;
- timestamps;
- user-entered preferences;
- saved operation outcomes.

Avoid storing full raw authenticated payloads indefinitely.

## Threat model

Threats include:

- accidental Git secret commit;
- malicious local webpage attempting to call the local server;
- XSS from API-derived strings;
- compromised local process reading secrets;
- dependency vulnerability;
- unauthorized LAN access if binding changes;
- stale/corrupt market data causing bad decisions.

## Read-only enforcement

The application should not expose an HTTP client abstraction that can issue arbitrary authenticated methods/URLs. Restrict the API adapter to a typed allow-list of known GET resources.

The project should include a regression test that no write-capable API method exists in the GW2 adapter surface.
