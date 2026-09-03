# Milestone Context - M11: Published Snapshot Reliability

## User outcome

The public Pages site receives a fresh complete market snapshot about every 15
minutes without depending on GitHub's best-effort native schedule. If an
external dependency fails, the site visibly pauses stale recommendations and
the owner can still dispatch the trusted workflow manually.

## Owner-approved decisions

- Cloudflare Worker Cron is the sole periodic scheduler, at minutes 7, 22, 37,
  and 52 UTC.
- The Worker authenticates with a GitHub App installed only on this repository,
  with no webhook subscriptions and only Actions write permission.
- The Worker dispatches only `pages.yml` on `develop`; GitHub Actions remains
  the sole live Guild Wars 2 client.
- The Worker has no public route and stores its App ID, installation ID, PKCS#8
  private key, and activation flag only as encrypted Cloudflare Worker secrets.
- Existing platform failure alerts are the operational signal. No application
  telemetry, personal-data collection, or notification service is introduced.
- A new Pages dispatch never cancels an active capture; it supersedes only an
  older pending refresh.

## Invariants

- Preserve the static browser, typed gateway, read-only boundary, `BigInt`
  financial calculations, selector validation, static artifact audit, and
  Pages permission split from M10.
- Never make Cloudflare, browser, or Worker requests to Guild Wars 2. Never
  expose or log a GitHub App credential, token, response body, snapshot, or
  player data.
- Preserve resolved VERIFY-014 history. Add and resolve VERIFY-015 only after
  real external-Cron evidence; do not change the open Guild Wars 2 API entries.

## Required owner rollout

Before merging the cutover PR, the owner prepares the repository-scoped GitHub
App, encrypted Worker secrets, a Cloudflare deployment session, and Cloudflare
and GitHub failure alerts. The merge causes a fresh push deployment; the owner
must deploy and enable the Worker before that snapshot reaches 30 minutes.

After deployment, record two consecutive Cron dispatches and their successful
GitHub workflow-dispatch runs before resolving VERIFY-015.
