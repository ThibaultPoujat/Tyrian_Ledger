# ADR-006 - Local Secret Storage

## Status
Accepted

## Decision
API credentials are stored outside source code using an OS-backed secret mechanism.

The local web host selects the supported store for its operating system:

- macOS Keychain on macOS;
- Windows Credential Manager on Windows;
- the freedesktop.org Secret Service API on Linux (for example, GNOME Keyring
  or KWallet).

The browser is never a secret-store client: it receives only non-secret
configuration state over the local Web API. An unsupported or unavailable OS
secret service produces the stable `LocalConfigurationError`; it must not cause
a plaintext-file fallback.

## Development fallback
An environment variable may be used for local development and test execution only; it must never be committed.
