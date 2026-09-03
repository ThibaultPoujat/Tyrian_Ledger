# Security and Data Protection Baseline

## Scope

The application is a public static market-analysis site. It has no account
system, credential store, local API, or server-side database.

The current static delivery must contain no personal data. If a future feature
collects, shares, hosts, or otherwise processes personal data, GDPR and French
data-protection obligations must be reassessed before that feature is enabled.

References:
- https://eur-lex.europa.eu/eli/reg/2016/679/oj
- https://www.cnil.fr/fr/securite-api-interfaces-de-programmation-applicative
- https://www.cnil.fr/fr/passer-laction/garantir-la-securite-des-donnees
- https://www.ssi.gouv.fr/entreprise/bonnes-pratiques/

## Mandatory/conditional requirements

1. Comply with current Guild Wars 2 API and website/content terms.
2. Apply GDPR/French data-protection obligations before introducing any
   personal-data processing.
3. Do not introduce API credentials, player data, or personal data into the
   static site, generator artifacts, source, logs, or tests.
4. Apply security controls proportionate to risk.
5. Reassess legal/security scope before public or hosted deployment.

## External scheduler boundary

The external Cloudflare Worker used to request periodic Pages captures is an
operations-only component, not an application runtime or Guild Wars 2 client.
It must not expose an HTTP endpoint, process player or browser data, fetch a
snapshot, or make any ArenaNet request. Its only permitted egress is GitHub's
API to create an installation token and dispatch the fixed `pages.yml` workflow
on `develop`.

The Worker uses an owner-created GitHub App installed on this repository only,
with no webhook subscriptions and only the required Actions write permission.
Its App ID, installation ID, and PKCS#8 private key live solely as encrypted
Cloudflare Worker secrets. They must never be copied into repository secrets,
workflow files, source, tests, fixtures, logs, screenshots, support messages,
or Git history. Disable the Worker before revoking or rotating the App key.

## M10 public static deployment gate

For M10, the repository and its complete Git history, the GitHub Pages site,
generated market snapshot, and build artifacts are public deployment material.
They must contain no credentials, tokens, API keys, player data, local paths,
or sensitive generated artifacts.

Before the owner makes the repository or Pages site public, the candidate
commit must pass this full-history audit from the repository root:

```sh
gitleaks detect --source . --log-opts="--all" --redact --no-banner
```

The delivery record must include the Gitleaks version, candidate commit,
command, scope, outcome, and targeted review of workflow files and the
assembled Pages artifact. A failed or incomplete audit blocks the public
deployment decision until the owner has reviewed and resolved it. This gate
does not authorize Codex to change repository visibility, Pages settings,
release state, or merge state.

## Best practices required by this project

- no credential or token support in the browser or generator;
- no secret in source, workflow files, snapshots, browser storage, logs, or
  fixtures;
- redacted logs;
- browser-local capital and risk preferences only;
- dependency updates;
- test fixtures free of real API keys;
- no analytics/telemetry sent remotely without explicit future decision;
- static-artifact and snapshot-contract validation before browser use;
- no unsafe HTML rendering of API-controlled names or text.

## Data minimization

Prefer storing:

- public market fields required by the versioned snapshot;
- snapshot generation timestamp and compatibility metadata;
- browser-local capital and risk preferences.

Do not store account identifiers, authenticated payloads, player history,
credentials, or server-side preference profiles.

## Threat model

Threats include:

- accidental Git secret commit;
- XSS from snapshot-derived strings;
- compromised publication workflow or generated artifact;
- dependency vulnerability;
- stale/corrupt market data causing bad decisions.

## Read-only enforcement

Only the scheduled snapshot generator may access Guild Wars 2 through a typed
allow-list of known GET resources. Browser code must not have an API client
for Guild Wars 2 or a local service.

The project should include a regression test that no write-capable API method exists in the GW2 adapter surface.
