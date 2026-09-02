# GitHub Pages static deployment

This is the operational guide for the single shared GitHub Pages deployment.
It complements the local-only [static development handoff](static-source-release.md).
The site contains only React assets and one public `market-snapshot.json`; it
has no local API, hosted API, account data, key, or player data.

## Trusted workflow design

`.github/workflows/pages.yml` runs on pushes to `develop` and at minutes 7,
22, 37, and 52 of every UTC hour. The offset still produces a 15-minute
capture interval while avoiding the top-of-hour delay risk documented by
GitHub. Scheduled runs use the default branch; this repository's default is
`develop`.

Each run is exclusive (`pages-publication`, cancelling an older in-progress
run) and performs these stages:

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
the documented Pages deployment permissions.

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
ticket. Once the owner has deliberately enabled Pages after that gate, inspect
the first push-triggered run and a later scheduled run, record their source
selection, capture-policy log, artifact audit, deployment URL, and snapshot
timestamp under VERIFY-014. A schedule, Pages setting, or public-history audit
is not considered verified merely because the workflow source exists.

GitHub documents [full-SHA action pinning](https://docs.github.com/en/actions/reference/security/secure-use), the [minimum Pages deployment permissions](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages), and the fact that [scheduled runs use the default branch and can be delayed](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows).
