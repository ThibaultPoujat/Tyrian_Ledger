# ADR-009 - External Pages Snapshot Scheduler

## Status

**Superseded by ADR-010 on 2026-09-04.**

This ADR remains historical evidence explaining M11. Do not extend the
Cloudflare/GitHub Pages scheduler for the active personal-assistant product.

## Decision (historical)

M11 used a private Cloudflare Worker Cron as the periodic trigger for the
trusted GitHub Pages publication workflow. It dispatched `pages.yml` on
`develop`; GitHub Actions remained the only live Guild Wars 2 client. GitHub App
credentials were stored as encrypted Cloudflare Worker secrets and the Worker
had no public request surface.

The Pages workflow preserved a single-publication concurrency group and did not
cancel an active capture.

## Consequences for M12

The external scheduler, GitHub Pages publication path, and their operational
credentials are no longer part of the target runtime. TKT-M12-02 retires active
repository support for them while preserving useful historical/security
records. Before or with a merge that removes `pages.yml`, the owner must disable
the provisioned Cloudflare Worker and revoke its GitHub App installation/key so
it cannot continue dispatching the retired workflow. That external cleanup is
an owner-controlled operational action outside normal code changes.
