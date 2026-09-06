# Local ArenaNet API Key Setup

Tyrian Ledger keeps a dedicated ArenaNet API key outside browser storage,
application settings, SQLite, source control, and logs. The React interface
never accepts, stores, or sends the key.

Create a dedicated, read-only key at
<https://account.arena.net/applications>. Personal Trading Post readiness
requires the mandatory `account` permission and `tradingpost`. A key can be
revoked at any time through the same ArenaNet page.

## Persistent local storage

Store the key once in the credential vault for the operating-system user who
runs Tyrian Ledger. The local host reads only the matching entry; it does not
create plaintext files or fall back to application configuration.

| Platform | Store | Entry to create |
| --- | --- | --- |
| macOS | Keychain Access | Generic Password with service name `com.tyrianledger.gw2-api-key`; put the key in the password field. macOS may ask whether the local host may read it. |
| Windows | Credential Manager > Windows Credentials | Generic Credential with Internet or network address `TyrianLedger.Gw2ApiKey`; put the key in the password field. No administrator privilege is required. |
| Linux desktop | Secret Service (for example GNOME Keyring or KWallet) | A Secret Service item with attribute `service` set to `com.tyrianledger.gw2-api-key`; `secret-tool store --label='Tyrian Ledger ArenaNet API key' service com.tyrianledger.gw2-api-key` can create it interactively. |

If the vault is locked, denies access, is unavailable, or has no matching
entry, account features report an unavailable or unconfigured state. Public
local application features still start normally.

## Development and testing only

For a temporary Development or Testing session, the host accepts
`TYRIAN_LEDGER_GW2_API_KEY` from the process environment. Do not put a real key
in `.env`, shell history, source, application configuration, a fixture, or a
test snapshot. Production ignores this environment variable.

The Playwright suite explicitly runs in Testing with an empty value so it never
reads a developer's OS credential or contacts ArenaNet.
