# Retired credential cleanup

M9 does not read, store, or require a Guild Wars 2 API credential. The M9
upgrade cannot safely delete an operating-system secret on the user's behalf.

If a pre-M9 installation configured a Tyrian Ledger credential, remove the
entry manually after the application has stopped:

- macOS: delete the Keychain generic-password item whose service is
  `com.tyrianledger.gw2-api-key`.
- Windows: delete the Credential Manager entry named
  `com.tyrianledger.gw2-api-key`.
- Linux desktop: remove the Secret Service entry with application
  `tyrian-ledger` and credential `gw2-api-key`.

This cleanup affects only the retired credential. It does not alter the local
SQLite preferences database, and the M9 application never writes to an OS
secret store.
