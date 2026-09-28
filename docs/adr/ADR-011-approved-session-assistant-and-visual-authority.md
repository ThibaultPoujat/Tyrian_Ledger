# ADR-011 — Session Assistant and Versioned Visual Authority

## Status

Accepted by the owner in the product/UX iteration, recorded 2026-09-28 in P00.
This amends product/distribution and visual-reference interpretation of ADR-010;
the stack, read-only game boundary, local privacy and financial authority remain.

## Context

The owner wants an assistant on a Windows landscape second monitor that offers
worthwhile actions while leaving most time for gameplay. Earlier docs, the Sites
prototype and the first image concepts describe different navigation and
interactions. Chat-only design approvals cannot reliably guide later AI sessions.
Local-first was also being interpreted as permanently personal-only deployment.

## Decision

- Version the six latest approved PNGs in Git. Their manifest identifies each
  screen and original bytes. `docs/ux/tyrian-ledger-visual-reference.md` owns
  visual acceptance and records behavioral corrections to illustrative mockups.
- Adopt `Signaux / Plans / Bilan`, with `Réglages` at the bottom, for a 1920×1080
  target. UI tickets must open original images and supply actual screenshots
  for independent comparison. The old reference remains historical only.
- Session preferences support active-work duration and liquid-gold deadline,
  with user-selected capital, risk, worthwhile-gain thresholds and activities.
  The assistant stays quiet except for urgent actionable conditions.
- Automatic API evidence is primary. Exceptional local completion is provisional,
  idempotent and persistent; safe continuation need not wait for cache expiry.
  The API remains authoritative for facts it exposes, with freshness/completeness
  required before treating evidence as a material contradiction.
- Include all characters' supported inventory/crafting capability; equipped and
  template equipment is never consumable stock. Unknown protection coverage is
  not permission to dispose of an asset.
- Local-first describes computation and private-data ownership. Friend installs
  and possible public distribution after 1.0 are compatible with it. No hosted
  account service, telemetry service or LAN exposure is thereby authorized.
- A future Windows tray companion maintains local monitoring after the UI closes.
  Explicit quit, OS permissions, sleep/offline and resume behavior remain honest.
  Implementation/package choice requires a bounded ticket, not a stack rewrite.
- AI-first means AI performs development. Runtime economics remain deterministic.

## Consequences

P00 aligns authority; P01–P07 implement and verify convergence incrementally.
Current code and old merged tickets describe historical behavior, not proof of
compliance with the new target. No runtime behavior changes in this ADR's PR.
The existing owner-merge protocol and model guide govern each child ticket.
The owner need not repeat the prototypes or manually maintain technical handoff.
