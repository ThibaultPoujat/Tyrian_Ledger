# Security review and secret-leak audit

## TKT-M8-01 sign-off — 2026-08-29

### Repository and delivery surfaces

- [x] Gitleaks scans full Git history in pull-request and `develop` CI.
- [x] The ticket branch was scanned locally before delivery.
- [x] The only initial scanner finding was the exact, non-secret source line
  that declares the environment-variable name; a line-anchored allowlist entry
  documents that false positive without excluding credential values elsewhere.
- [x] No API credential, private account payload, local database, log, or
  secret-file artifact is tracked by Git.
- [x] `.env` files, local runtime data, SQLite files, logs, and developer-local
  configuration remain ignored.

### Browser and API boundary

- [x] Browser requests to `/api/*` have no authorization header, credential in
  the URL, or credential in their request body.
- [x] The configured-credential status and account-access responses expose only
  safe status or token metadata; automated tests use synthetic credentials and
  assert that they are absent from response bodies.
- [x] Token metadata is treated as text by the frontend test suite.

### Logging, exceptions, and local runtime

- [x] Secret-store tests verify that synthetic credentials are absent from
  logged messages and thrown configuration errors.
- [x] Account gateway code keeps the credential in an HTTP authorization header
  and handles transport, parsing, and credential-store failures as stable
  application states rather than propagating their details to clients.
- [x] The default server URL is `http://127.0.0.1:5000`; integration tests
  verify the loopback default and local HTTP security headers.

## Remaining risks

- An explicit `--urls` or `ASPNETCORE_URLS` override can bind beyond loopback.
  This remains a documented developer escape hatch and must not be used for
  normal operation.
- The local-only HTTP host intentionally does not configure TLS or HSTS.
- Gitleaks and these tests reduce accidental disclosure; they do not guarantee
  that all vulnerabilities or sensitive-data paths are absent.
