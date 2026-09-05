# ADR-008 - Static GitHub Pages Snapshot Hosting

## Status

**Superseded by ADR-010 on 2026-09-04.**

This ADR remains historical evidence explaining the M10 public static-site
architecture. Do not use it as active product guidance after the M12 pivot.

## Decision (historical)

For the M10 static delivery, Tyrian Ledger deployed as one public static GitHub
Pages site. GitHub Actions was the only live Guild Wars 2 client and generated a
publishable public market snapshot through the typed gateway. GitHub Pages
served static React assets and `market-snapshot.json`; it did not host an
ASP.NET API, SQLite database, server-side preference store, or browser path to
Guild Wars 2/local `/api` endpoints.

The repository and its history were treated as public deployment material with
explicit selector, artifact, and secret-scan controls.

## Historical supersession relationship

This ADR superseded the local-runtime assumptions in ADR-001/ADR-002 only for
the M10 public delivery experiment. ADR-010 now supersedes this ADR for the
active personal product and reaffirms ASP.NET Core + React + SQLite local-first
runtime.

The decisions that remain independently active are still governed by their own
ADRs, notably the typed gateway, integer-copper money, secret boundary, and
read-only Guild Wars 2 boundary.

## Consequences for M12

Pages workflows, public snapshot publication, selector machinery, and related
static-runtime code may remain temporarily until TKT-M12-02 retires them. Their
presence does not authorize new features on the static architecture.
