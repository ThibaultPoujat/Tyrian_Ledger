# Security and Data-Protection Baseline

## Scope

Tyrian Ledger is a **local-first personal application**. It processes the
owner's read-only ArenaNet account/Trading Post data, stores normalized personal
history and settings in local SQLite, stores owned public-market history, and
serves React through a local ASP.NET Core host.

The target host binds loopback only by default. There is no public hosted API or
cloud database in V1. Any future LAN/Internet exposure, cloud sync, telemetry,
or multi-user deployment requires a separate owner-approved architecture,
security, and data-protection review.

M10-M11 public Pages/Cloudflare delivery is superseded historical architecture.
Its code may remain only during M12 transition and must not be extended as the
active personal runtime.

## Core security requirements

1. **Read-only Guild Wars 2 boundary.** No gameplay automation, Trading Post
   mutation, order placement, cancellation, or update API capability.
2. **Typed gateway only.** Feature code never constructs ArenaNet URLs or
   authorization requests directly.
3. **Secret isolation.** The ArenaNet key belongs to the OS-backed secret store
   in ADR-006. The browser and SQLite are never secret stores.
4. **Loopback by default.** Local HTTP endpoints must not be exposed to LAN or
   Internet by normal configuration.
5. **Data minimization.** Persist normalized personal fields required by product
   behavior, not indefinite raw private account payloads.
6. **Safe failure.** Raw upstream exceptions/headers/private payloads do not
   leak into browser responses or logs.
7. **Local ownership.** Backups remain user-controlled local artifacts unless a
   future approved design says otherwise.
8. **No silent analytics.** No remote telemetry/analytics containing personal
   or trading data without an explicit owner decision and data-protection review.

## Secret boundary

Never place credential/token/authorization values in:

- source or Git history;
- `.env` files committed to Git;
- frontend code, browser local/session storage, service workers, or browser
  payloads;
- SQLite or generated market-history databases;
- logs, exceptions, screenshots, fixtures, test snapshots, prompts, PR bodies,
  issue comments, or support messages;
- public artifacts or backups intended for sharing.

Supported production/local secret storage follows ADR-006. Environment
variables are development/test fallback only and must not cause a plaintext
config-file fallback.

Token/account metadata returned by ArenaNet is untrusted text. Never render it
as raw HTML.

## Private account-data handling

Personal TP transactions, current-order observations, account scope, inventory
and crafting data are private local application data.

- Fetch only endpoints required for enabled features.
- Normalize before persistence when practical.
- Keep account scope explicit.
- Preserve completed transaction history intentionally because remote API
  history may be limited, but do not retain unrelated raw payloads indefinitely.
- Make clear/backup/restore operations deliberate and safe.
- Avoid logging itemized private payloads unless a synthetic/sanitized fixture
  is explicitly created for development.

## SQLite and recovery

The database becomes irreplaceable owned history after remote transaction
windows age out. Therefore:

- use versioned tested migrations;
- use transaction-safe writes where partial state would be harmful;
- partial remote failure must not wipe last-known-good data;
- destructive migration/retention/clear behavior requires explicit safeguards;
- backup/restore must be tested against representative populated data;
- no backup is uploaded automatically.

## Local HTTP/browser boundary

The browser may call the local ASP.NET Core API but must receive only the
minimum safe data required by UI behavior.

Security review should verify actual data flow:

`OS secret store -> infrastructure HTTP auth -> ArenaNet -> normalized application result -> safe local API -> React`

The key must never flow in the reverse direction toward the browser.

CORS/origin and local binding should be as narrow as practical for the chosen
React development/production topology. Any explicit developer override that
binds beyond loopback is not a supported normal-use security posture.

## ArenaNet/API failure handling

Stable application errors should distinguish conditions such as missing
permission, invalid credential, rate limit, transport failure, invalid remote
data, incomplete data, and local configuration failure without embedding
secret-bearing response details.

Normal tests use synthetic fixtures/mocks, not live private credentials.

## Public-market/history integrity

Bad market data can cause financially bad advice even without a traditional
security exploit. Treat freshness, schema validation, partial captures,
impossible prices/quantities, timestamp integrity, and duplicate observations
as security/reliability concerns.

A failed/partial capture must never become a valid historical observation.

## Repository/CI hygiene

Full-history secret scanning remains valuable because this repository has
previously contained public deployment workflows and will contain code handling
private local data.

Use the repository's documented Gitleaks command/check in release/high-risk
validation. Test fixtures contain synthetic secrets only and tests should assert
those values do not appear in output/logs.

Dependency and action updates remain reviewed/pinned according to active CI
policy.

## Data protection

The owner is the intended single local user; V1 is not a hosted service. Still,
personal/account data should be minimized and secured by design. Reassess
applicable GDPR/French obligations before any feature begins collecting,
sharing, hosting, remotely backing up, or analyzing data beyond the owner's
local machine.

Reference material:

- GDPR: https://eur-lex.europa.eu/eli/reg/2016/679/oj
- CNIL API security guidance: https://www.cnil.fr/fr/securite-api-interfaces-de-programmation-applicative
- CNIL data-security guidance: https://www.cnil.fr/fr/passer-laction/garantir-la-securite-des-donnees

## Threat model

Key threats now include:

- accidental credential/Git/log/browser disclosure;
- local host accidentally exposed beyond loopback;
- XSS/untrusted upstream text;
- corrupted/destructive SQLite migration or sync;
- partial API failure deleting valid personal state;
- compromised dependency;
- stale/corrupt/manipulated market evidence producing bad recommendations;
- recommendation composition bypassing max-bid/cash/exposure constraints;
- unsafe restore/backup/clear flow.

Each relevant high-risk ticket receives a fresh independent review using the
Tyrian review skill.
