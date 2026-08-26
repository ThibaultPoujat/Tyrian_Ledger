# Local Secrets

Tyrian Ledger never reads a GW2 API credential from source files, ordinary
application configuration, browser storage, or persisted application data.

## Persistent local credential

Outside Development and Testing, the application retrieves its credential from
the macOS Keychain. Create a generic-password item in Keychain Access with the
service name `com.tyrianledger.gw2-api-key`. The application intentionally has
no plaintext file fallback if that item is unavailable.

## Development and test override

For a temporary local-development or test session only, start the application
with `ASPNETCORE_ENVIRONMENT=Development` (or `Testing`) and set
`TYRIAN_LEDGER_GW2_API_KEY` in that shell's environment. For example:

```zsh
export ASPNETCORE_ENVIRONMENT=Development
read -rs "TYRIAN_LEDGER_GW2_API_KEY?GW2 API credential: "
print
export TYRIAN_LEDGER_GW2_API_KEY
dotnet run --project src/Gw2Tp.Web
```

Do not place a real key in `.env`, source code, application settings, test
fixtures, logs, command history, or browser storage. Close the shell or unset
the variable after the development session.
