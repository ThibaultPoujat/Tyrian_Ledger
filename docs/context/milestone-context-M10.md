# Milestone Context - M10: Static GitHub Pages Snapshot Deployment

## Read this context with

1. Repository-root `AGENTS.md`.
2. `docs/context/permanent-context.md`.
3. This document.
4. `docs/verification/VERIFY-REGISTER.md`.
5. The assigned M10 ticket.

## User outcome

Tyrian Ledger is available at a public GitHub Pages URL so the owner can test
work without running the application locally. The site is static and presents
the same deterministic market analysis from a periodically generated public
snapshot rather than from a player-triggered local scan.

## Owner-approved decisions

- The repository must be public before Pages is enabled; all source and Git
  history are therefore public deployment material.
- GitHub Actions is the sole live Guild Wars 2 client. It generates a complete
  market snapshot every 15 minutes under a 2 requests/second, two-concurrent,
  burst-20 policy.
- GitHub Pages serves React assets plus `market-snapshot.json` only. Browser
  code must never request Guild Wars 2 endpoints or a local `/api` endpoint.
- Capital and risk settings are browser-local only. Browser-side recommendations
  use `BigInt` and preserve integer-copper determinism.
- A snapshot older than 30 minutes is non-actionable: display a clear
  delayed-refresh state and no recommendations.
- There is one shared Pages deployment. A reviewed config-only selector PR on
  `develop` may select the immutable SHA of an open code PR in this repository.
  A trusted workflow validates the selection before it is published; invalid,
  closed, or merged selections use `develop` instead.
- M10 will adopt a new ADR that supersedes the local-hosting assumptions in
  ADR-001 and ADR-002. M9 is historical and must remain unchanged.

## M10 invariants

- Retain the typed gateway for every Guild Wars 2 access and retain the
  application-wide read-only boundary.
- Never put secrets in source, workflow files, generated snapshots, build
  artifacts, logs, tests, fixtures, pull requests, or Git history.
- Keep external DTOs distinct from domain models. Do not invent external API
  behaviour; update the relevant VERIFY entries when M10 evidence changes.
- Preserve deterministic financial calculations in integer copper. The browser
  counterpart must use `BigInt`, not JavaScript `number`, for money and fee
  arithmetic.
- Do not make browser-side Guild Wars 2, local API, or hidden server requests.
- Do not change repository visibility, Pages settings, release state, or merge
  state as an incidental implementation step; record the owner action and
  evidence when it is intentionally performed.

## Verification focus

M10 affects the unresolved external facts represented by VERIFY-004
(batching), VERIFY-005 (schema), VERIFY-006 and VERIFY-011 (rate limits),
VERIFY-010 (429/retry behaviour), and VERIFY-013 (fees). Tickets must update
the existing entries or add a narrowly scoped M10 entry only when supported by
new evidence. Do not convert a planning assumption into asserted external
fact.

The test pyramid changes over the milestone: C# contract and generator tests;
shared C#/browser golden vectors; static browser tests; workflow/configuration
validation; and a live Pages smoke test in the independent review. M10-06
also performs a full-history secret audit before release handoff.

## Required Codex configuration

The owner has approved a milestone-specific exception: every M10
implementation and review task must use **GPT-5.6 Terra with XHigh reasoning
effort**. This exception applies only to M10 and does not change the
repository-wide default configuration. Terra supports the `xhigh` reasoning
effort; see the [official model documentation](https://developers.openai.com/api/docs/models/gpt-5.6-terra).
Record the selected model and effort in each delivery report.

## Ticket handoff order

Execute tickets strictly in sequence:

1. TKT-M10-00 establishes the ADR, public-deployment security posture, M10
   documentation, and VERIFY baseline.
2. TKT-M10-01 creates the bounded collector, versioned snapshot contract, and
   generator CLI.
3. TKT-M10-02 provides browser-side parsing and exact recommendation parity.
4. TKT-M10-03 moves the user experience and browser tests to static snapshots.
5. TKT-M10-04 removes superseded local-server and persistence infrastructure.
6. TKT-M10-05 makes the static deployment and trusted selector workflow live.
7. TKT-M10-06 independently reviews the public deployment and prepares the
   release handoff.

Do not begin a later ticket by assuming implementation details that an earlier
ticket has not yet accepted. Use the prior ticket's merged contract, tests,
VERIFY evidence, and pull request as the handoff.
