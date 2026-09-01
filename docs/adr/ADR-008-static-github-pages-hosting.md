# ADR-008 - Static GitHub Pages Snapshot Hosting

## Status

Accepted

## Decision

For the M10 static delivery, Tyrian Ledger will deploy as one public static
GitHub Pages site. GitHub Actions will be the only live Guild Wars 2 client
and will generate the publishable public market snapshot through the existing
typed gateway. GitHub Pages will serve only static React assets and
`market-snapshot.json`; it will not host an ASP.NET API, SQLite database,
server-side preference store, or browser path to Guild Wars 2 or local `/api`
endpoints.

The site has one shared deployment. A reviewed, config-only pull request
targeting `develop` may select the immutable SHA of an open code pull request
from this repository. A trusted publishing workflow validates that selection
independently before use. Missing, malformed, mutable, cross-repository,
closed, merged, or otherwise invalid selections publish the current `develop`
SHA instead. Untrusted pull-request code cannot control the publishing
workflow or obtain publishing credentials.

The repository and its complete Git history, the Pages site, generated
snapshot, and build artifacts are public deployment material. Before the owner
makes the repository or Pages site public, the candidate commit must pass this
full-history audit from the repository root:

```sh
gitleaks detect --source . --log-opts="--all" --redact --no-banner
```

The delivery evidence must also record the Gitleaks version, candidate commit,
command, scope, result, and a targeted review of workflow files and the
assembled Pages artifact. The owner alone decides whether to change repository
visibility, enable Pages, publish a release, or merge a pull request.

## Supersession and preserved decisions

This ADR supersedes only the local runtime and deployment assumptions in
ADR-001 and ADR-002: an ASP.NET Core loopback host and SQLite are no longer the
public delivery runtime, and V1 no longer requires a user-operated local
server. The original ADRs remain historical records outside those assumptions.

ADR-004's typed Guild Wars 2 gateway, ADR-005's integer-copper money model,
ADR-006's secret boundary, and ADR-007's GET-only read-only boundary remain
accepted and unchanged. Browser-side recommendation calculations retain exact
integer semantics with `BigInt`; no secret, player data, or API key may enter
source, workflows, logs, snapshots, artifacts, browser storage, or Git
history.

## Consequences

Static delivery removes the hosted local-server and persistence surfaces from
the public site when TKT-M10-04 retires the local runtime; this ADR makes no
runtime change itself. Snapshot collection, trusted publication, selector
validation, and the public-history gate require explicit M10 evidence before
the owner performs any public deployment action.
