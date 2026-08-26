# Local Secrets

Tyrian Ledger never reads a GW2 API credential from source files, ordinary
application configuration, browser storage, or persisted application data.

## Browser boundary

Secret storage belongs to the local Web API host, not the browser. The browser
can use the application normally on every supported host platform because it
only receives non-secret configuration status; no credential is sent in HTML,
JavaScript, browser storage, or API responses.

## Persistent local credential

Outside Development and Testing, the application reads a credential from the
current host's OS-backed store. It intentionally has no plaintext-file
fallback.

### macOS

In Keychain Access, create a generic-password item with the service name
`com.tyrianledger.gw2-api-key`. Put the API credential in the password field.

### Windows

Use the built-in `cmdkey` command to create the generic credential. Omitting
`/pass` prompts for the credential value instead of placing it in shell
history:

```powershell
cmdkey /generic:com.tyrianledger.gw2-api-key /user:TyrianLedger
```

### Linux desktop

The application uses the freedesktop.org Secret Service API through
`secret-tool` (provided by `libsecret-tools`) and works with compatible
services such as GNOME Keyring or KWallet. With an unlocked secret service,
store the credential using this command; it prompts for the value without
placing it in shell history:

```sh
secret-tool store --label='Tyrian Ledger GW2 API credential' \
  application tyrian-ledger credential gw2-api-key
```

If the Secret Service is not installed, running, or unlocked, the application
returns `LocalConfigurationError`. It does not fall back to a plaintext file.

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

## References

- [Windows Credential Manager `CredReadW`](https://learn.microsoft.com/windows/win32/api/wincred/nf-wincred-credreadw)
- [Windows `cmdkey` command](https://learn.microsoft.com/windows-server/administration/windows-commands/cmdkey)
- [Linux `secret-tool` manual](https://manpages.debian.org/unstable/libsecret-tools/secret-tool.1.en.html)
- [freedesktop.org Secret Service API](https://specifications.freedesktop.org/secret-service/latest/)
