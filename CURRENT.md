# Current Project State

Last durable-context update: 2026-09-28.

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
and Draft gates come only from `docs/workflow/model-effort-guide.md`; the first
bounded coding prompt is in `docs/workflow/goal-session.md`.

GitHub owns operational state; #98 and `docs/milestones/INDEX.md` own order; the
model guide owns active Sol gates. Reconcile these at session start. The block
below is derived, not an independent authority or a replacement for readiness.
The deterministic post-merge updater changes only that block. The owner should
not maintain handoff state manually.

## Corrective program readiness

[Approved packages P00–P07](docs/milestones/approved-delivery-plan.md) map the
19 audit findings. #148 prepares the repository. #149 is the first bounded
coding ticket after #148 merges and addresses duplicate resource demand only.
#150 is a tracking/checkpoint gate, **not a coding Goal**. After #149, a planning
checkpoint prepares the next small Ready child and updates both order sources.
Do not skip unresolved packages to #96/#97. Historical merged features are not
proof that newly discovered issues were fixed.

The audited baseline is develop `23be2fed09c6d5a42cfbd39f65d96831c30da526`.
Read current GitHub state for newer merges. Relevant upstream uncertainties
remain in VERIFY; neither mocks nor elapsed cache time establish API truth.

## Generated live state

Machine-owned derived values. Do not rewrite durable context for routine progress.

<!-- BEGIN GENERATED LIVE STATE -->
- Last completed implementation ticket: `TKT-M22-C01 / #153`
- Last merged implementation PR: `#155`
- Active milestone: `M22 — Convenience, Hardening, and Evaluation`
- Preferred next implementation ticket: `TKT-M22-P01B / #154`
- Next required checkpoint: `None`
- Allowed non-blocking alternate: `None`
- Explicit active Sol gates: `TKT-M22-P01B / #154`, `TKT-M22-02 / #96`
- Authorities: operational state = `GitHub`; execution order = `issue #98 + docs/milestones/INDEX.md`; review gates = `docs/workflow/model-effort-guide.md`
<!-- END GENERATED LIVE STATE -->
