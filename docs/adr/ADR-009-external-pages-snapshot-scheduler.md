# ADR-009 - External Pages Snapshot Scheduler

## Status

Accepted

## Decision

Cloudflare Worker Cron is the production authority for the periodic trigger of
the existing trusted Pages publication workflow. It dispatches `pages.yml` on
the fixed `develop` branch at minutes 7, 22, 37, and 52 of every UTC hour. The
GitHub Actions `schedule` event is removed; `push` to `develop` and the manual
`workflow_dispatch` recovery path remain.

The Worker is private (`workers_dev: false`) and has no request-handling
surface. It neither contacts Guild Wars 2 nor reads the public snapshot. On an
enabled Cron invocation it creates a short-lived GitHub App installation token
and calls only the GitHub API endpoint that dispatches the fixed `pages.yml`
workflow on `develop`.

The owner creates a GitHub App with no webhook subscriptions, installed only on
this repository, with only Actions write permission. Its App ID, installation
ID, and PKCS#8 private key are encrypted Cloudflare Worker secrets. The App
installation is repository-scoped but GitHub's Actions permission is not
workflow-scoped; Worker source, tests, and review lock its only dispatch target
to `pages.yml` on `develop`.

The Pages workflow preserves its single-publication concurrency group but does
not cancel a running capture. A later dispatch replaces only an older pending
refresh. This favors publishing a completed trusted artifact over interrupting
a slow capture.

## Consequences

This supersedes only ADR-008's reliance on GitHub's native schedule for the
15-minute trigger. ADR-008's static-site, typed-gateway, selector, artifact,
and public-history boundaries remain unchanged. GitHub Actions remains the sole
live Guild Wars 2 client; the browser remains static and makes no Guild Wars 2
or `/api` request.

Cloudflare and GitHub remain external services and cannot provide absolute
delivery guarantees. Cloudflare Worker error alerts, GitHub Actions failure
notifications, the 30-minute stale-snapshot safety state, and manual dispatch
are layered recovery controls. The owner provisions, rotates, revokes, and
deploys the external credentials and service; no credential is added to this
repository or a GitHub Actions workflow.
