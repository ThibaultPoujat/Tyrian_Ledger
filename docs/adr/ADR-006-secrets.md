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

## Scope boundary

This ADR governs Tyrian Ledger's single-user, loopback-only local runtime. It
does not authorize a shared-server, Internet-hosted, or multi-tenant deployment
to reuse desktop credential-vault assumptions. Before any such deployment, the
owner must approve a successor ADR covering server-side secret management,
identity and authentication, tenant isolation, retention, incident response,
and the ArenaNet API-key policy. This ADR creates no hosted-product commitment.

## Development fallback
An environment variable may be used for local development and test execution only; it must never be committed.
