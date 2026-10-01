# Current Project State

Last durable-context update: 2026-10-01 (B2 batch preparation).

## Durable product direction

Tyrian Ledger is a local-first Windows 1920×1080 second-screen Guild Wars 2
profit assistant. It offers worthwhile manual actions within the player's
session time, capital, risk and activity choices. It stays quiet except for
urgent actionable alerts. AI develops the code; the runtime financial engine is
deterministic, integer-copper based and read-only toward Guild Wars 2.

The owner is the first user; friend/public distribution remains possible.
Local-first does not restrict the product to one private installation.

Target navigation: `Signaux / Plans / Bilan`, with `Réglages` at the bottom.
The [approved contract](docs/specs/approved-product-direction.md) specifies both
time objectives, exceptional local completion with later API reconciliation,
all-character coverage and equipped/template protections. The
[six-image baseline](docs/ux/tyrian-ledger-visual-reference.md) is mandatory for
UI implementation/review. Older concepts and the Sites prototype are superseded.
These are target requirements; this preparation does not implement the new UI
or repair the audited runtime findings.

Target runtime remains:

`React -> loopback ASP.NET Core -> deterministic Application/Analytics -> SQLite + typed read-only ArenaNet gateway`

## Durable delivery rules

One Ready ticket per Goal/session, one isolated branch/worktree, required tests
and independent review, then PR/handoff and stop. Owner merges. Model selection
and Draft gates come only from `docs/workflow/model-effort-guide.md`; the reusable
next-Ready-ticket prompt is in `docs/workflow/goal-session.md`.

GitHub owns operational state; #98 and `docs/milestones/INDEX.md` own order; the
model guide owns active Sol gates. Reconcile these at session start. The block
below is derived, not an independent authority or a replacement for readiness.
The deterministic post-merge updater changes only that block. The owner should
not maintain handoff state manually.

## Corrective program readiness

[Approved packages P00–P07](docs/milestones/approved-delivery-plan.md) retain all
19 audit findings. The [remaining map](docs/milestones/remaining-delivery-map.md)
defines dependencies and exits through release; [B2](docs/milestones/M22/batch-02.md)
contains the current four prepared contracts. Later work remains Planned.
B1 is merged; owner UI changes are needed and usability acceptance remains open.

Astra prepares 3–5 contracts at a batch checkpoint, not after each merge. Each
new Goal verifies its predecessor merge/evidence and entry conditions, implements
one prepared ticket and stops for owner merge. #150 is the batch/integration
checkpoint, not a coding Goal; it remains open through P01–P06 acceptance.
No automatic multi-ticket execution or skip to #96/#97. Historical merged
features are not proof that newly discovered issues were fixed.

The audited baseline is develop `23be2fed09c6d5a42cfbd39f65d96831c30da526`.
Read current GitHub state for newer merges. Relevant upstream uncertainties
remain in VERIFY; neither mocks nor elapsed cache time establish API truth.

## Generated live state

Machine-owned derived values. Do not rewrite durable context for routine progress.

<!-- BEGIN GENERATED LIVE STATE -->
- Last completed implementation ticket: `TKT-M22-C03 / #167`
- Last merged implementation PR: `#172`
- Active milestone: `M22 — Convenience, Hardening, and Evaluation`
- Preferred next implementation ticket: `TKT-M22-P02A / #168`
- Next required checkpoint: `None`
- Allowed non-blocking alternate: `None`
- Explicit active Sol gates: `TKT-M22-P03A / #170`, `TKT-M22-02 / #96`
- Authorities: operational state = `GitHub`; execution order = `issue #98 + docs/milestones/INDEX.md`; review gates = `docs/workflow/model-effort-guide.md`
<!-- END GENERATED LIVE STATE -->
