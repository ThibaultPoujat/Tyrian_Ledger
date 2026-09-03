# GitHub Pages static deployment

This is the operational guide for the single shared GitHub Pages deployment.
It complements the local-only [static development handoff](static-source-release.md).
The site contains only React assets and one public `market-snapshot.json`; it
has no local API, hosted API, account data, key, or player data.

## Trusted workflow design

`.github/workflows/pages.yml` runs on pushes to `develop` and manually through
the Actions UI. The independent Cloudflare Worker Cron in
`ops/pages-snapshot-scheduler` is the sole periodic trigger: at minutes 7, 22,
37, and 52 of every UTC hour it dispatches this same workflow on `develop`.
GitHub Actions remains the only live Guild Wars 2 client.

Each run is exclusive (`pages-publication`) and performs these stages. A newer
dispatch never cancels an in-progress capture; GitHub retains only the newest
pending refresh while that capture finishes.

1. Resolve the reviewed selector from trusted `develop` code.
2. Generate the snapshot and build React assets from the selected immutable
   source in a `contents: read` job only.
3. Download that candidate into a separate trusted job, reject anything other
   than static React assets plus a validated v1 snapshot, and check for
   credential-shaped text, local paths, `/api`, and Guild Wars 2 browser
   endpoints.
4. Download and audit the same artifact again in the only job with
   `pages: write` and `id-token: write`, then package and deploy it.

The unprivileged build may execute a reviewed same-repository pull request to
make a compatible preview, but it never has Pages or OIDC permission. The
deployment job checks out `github.sha` from `develop` and never executes
selected pull-request code. There is no `pull_request_target` trigger and no
repository, organization, or user secret. GitHub's short-lived job token is
used only for repository read access, selector lookup, artifact transfer, and
the documented Pages deployment permissions. The Worker is not part of this
trust boundary: it cannot read the snapshot, access Guild Wars 2, or deploy to
Pages. It can only ask GitHub to start this workflow on `develop`.

## External scheduler

The private Cloudflare Worker has no public route (`workers_dev: false`) and
uses only its `scheduled()` handler. Its Cron expression is
`7,22,37,52 * * * *` (UTC). It uses a GitHub App installed only on this
repository, with no webhook subscriptions and only **Actions: write**
permission. For each invocation it creates a short-lived App installation token
and posts the fixed `workflow_dispatch` request for `pages.yml` with
`{ "ref": "develop" }`.

`GITHUB_APP_ID`, `GITHUB_APP_INSTALLATION_ID`, and the PKCS#8
`GITHUB_APP_PRIVATE_KEY` are encrypted Cloudflare Worker secrets, never source
or GitHub Actions secrets. `SCHEDULER_ENABLED` is also a Worker secret; only
the exact value `true` permits a dispatch. The Worker logs only the operation
and HTTP status, never a credential, authorization header, or response body.

Before merging a scheduler cutover, the owner must create the App and install
it only on `Tyrian_Ledger`, prepare Cloudflare Worker secrets and platform
error alerts, and have a validated `wrangler deploy` session ready. The merge
itself produces a fresh push deployment. Immediately afterwards the owner
deploys the Worker, sets the secrets, and enables it before that snapshot
reaches the 30-minute delayed threshold. Cloudflare Worker error alerts and
GitHub Actions failure notifications are the operational failure signals.

To rotate or revoke access, the owner disables `SCHEDULER_ENABLED`, revokes the
GitHub App key or removes the App installation, replaces the Cloudflare secret,
then re-enables only after a successful manual dispatch verification. The
manual **Run workflow** path remains available throughout.

Every third-party action is pinned to a full commit SHA, with a release-family
comment next to the pin. The current immutable pins cover checkout, .NET and
Node setup, artifact transfer, Pages configuration/upload/deployment, and the
existing Gitleaks scan. Update a pin only after verifying the new SHA belongs
to its upstream action repository, then keep the workflow-contract test green.

## Reviewed preview selector

The default `.github/pages-preview-selector.json` is:

```json
{
  "schemaVersion": 1,
  "selection": null
}
```

`null` publishes the current `develop` SHA. To preview an open code pull
request, create a separate, reviewed, config-only pull request targeting
`develop` that changes `selection` to:

```json
{
  "schemaVersion": 1,
  "selection": {
    "pullRequestNumber": 123,
    "headSha": "0123456789abcdef0123456789abcdef01234567"
  }
}
```

The SHA must be exactly 40 lowercase hexadecimal characters; branch names,
tags, refs, and additional fields are rejected. Before using it, the trusted
workflow requests that pull request through GitHub's API and requires all of:

- open state and no merge timestamp;
- base branch `develop`;
- a head repository exactly equal to this repository; and
- a head SHA exactly equal to the selected immutable SHA.

An absent, malformed, mutable, cross-repository, closed, merged, or
unavailable selection falls back to the current `develop` SHA. To stop a
valid selected preview, merge a config-only change back to `selection: null`.
The next successful capture publishes `develop` again.

## Failure, rollback, and caching behaviour

The generator writes only a complete snapshot. If capture, build, artifact
assembly, or audit fails, the deployment job does not run and GitHub Pages
continues to serve the last successful deployment. Clear the selector or
revert the relevant `develop` change to trigger a new known-good deployment;
this workflow never promotes an arbitrary historical artifact.

When the displayed snapshot is delayed, an owner may use **Run workflow** in
the Actions UI to invoke the `workflow_dispatch` recovery path on `develop`; it
follows the same trusted selection, capture, audit, and deployment stages as
the external Cron dispatch. The native GitHub `schedule` event is intentionally
absent, because it can be delayed or dropped under load.

The React build is given the project Pages base path and a SHA-256 revision of
the generated snapshot. It fetches that snapshot with `cache: 'no-store'`.
GitHub Pages and browser caches can still delay the new HTML or bundle, so the
timestamp in `market-snapshot.json` is authoritative: the UI pauses
recommendations once it is older than 30 minutes. A delayed scheduled run is
therefore visible rather than silently actionable.

## Validation and owner gate

The regular CI workflow runs the selector, artifact, pinning, permission, and
trigger-contract tests. For a local non-production equivalent of the artifact
assembly, use the checked-in synthetic snapshot after a normal browser build:

```sh
npm --prefix frontend ci
VITE_SITE_BASE_PATH=/Tyrian_Ledger/ VITE_MARKET_SNAPSHOT_PATH='market-snapshot.json?revision=fixture' npm --prefix frontend run build
pages_root="$(mktemp -d)"
node .github/scripts/assemble-pages-artifact.mjs --dist frontend/dist --snapshot tests/fixtures/market-snapshots/market-snapshot-v1.json --output "$pages_root/pages"
node .github/scripts/audit-pages-artifact.mjs "$pages_root/pages"
```

Before advising the owner to make the repository or Pages public, record the
candidate SHA, Gitleaks version, outcome, and exact command for this mandatory
full-history audit, plus a targeted workflow and assembled-artifact review:

```sh
gitleaks detect --source . --log-opts="--all" --redact --no-banner
```

Do not change repository visibility or enable/configure Pages as part of this
ticket. M10's native-schedule evidence remains recorded under VERIFY-014. M11
must separately record two consecutive Cloudflare Cron dispatches, their
resulting GitHub runs, source selection, capture policy, artifact audits,
deployment URLs, and snapshot timestamps under VERIFY-015. An external
scheduler is not verified merely because its Worker source exists.

GitHub documents [full-SHA action pinning](https://docs.github.com/en/actions/reference/security/secure-use), the [minimum Pages deployment permissions](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages), and the fact that [scheduled runs use the default branch and can be delayed](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows).
