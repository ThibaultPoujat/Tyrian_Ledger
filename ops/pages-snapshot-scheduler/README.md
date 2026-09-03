# Tyrian Ledger Pages snapshot scheduler

This private Cloudflare Worker Cron dispatches the existing trusted Pages
workflow every 15 minutes. It is not an application server and never contacts
Guild Wars 2, the public Pages site, or a browser client.

## Owner setup

1. Create a GitHub App with no webhook subscriptions, install it only on
   `ThibaultPoujat/Tyrian_Ledger`, and grant only **Actions: write** repository
   permission.
2. Generate an App private key in PKCS#8 PEM form. Keep the key outside this
   repository and GitHub Actions. If GitHub provides an RSA PEM, the owner can
   make a PKCS#8 copy locally with `openssl pkcs8 -topk8 -nocrypt -in key.pem
   -out key-pkcs8.pem`, then protect and delete the temporary files according
   to their local secret-handling policy.
3. Authenticate `wrangler` to the owner's Cloudflare account, run `npm ci`,
   then run `npm run deploy` from this directory.
4. Set the four encrypted Worker secrets in Cloudflare: `GITHUB_APP_ID`,
   `GITHUB_APP_INSTALLATION_ID`, `GITHUB_APP_PRIVATE_KEY`, and
   `SCHEDULER_ENABLED`. Set the final value to `true` only after the first
   three are in place.
5. Configure a Cloudflare Worker error alert and GitHub Actions failure
   notifications. Verify two consecutive Cron dispatches create successful
   `workflow_dispatch` Pages runs before considering the cutover complete.

`workers_dev` is disabled and the Worker returns HTTP 404 for any accidental
request. Logs contain only operation names and HTTP status codes. To revoke or
rotate the App key, set `SCHEDULER_ENABLED` away from `true` before changing
the App installation or private-key secret.
