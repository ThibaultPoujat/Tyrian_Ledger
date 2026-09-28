# Approved Visual Baseline — 2026-09-28

Status: **owner-approved target; mandatory for new UI work**.

The six PNGs below are the latest approved iteration. They replace the first
three concepts, the 2026-09-22 reference and the external Sites prototype. An
agent must open the relevant local images, not rely on chat memory or this prose
alone. These are design references, not screenshots of implemented software.

## Authority and preservation

[Approved product direction](../specs/approved-product-direction.md) owns behavior.
[UX](ux.md) owns interactions. This document and its image set own appearance.
Verified upstream contracts, financial and security invariants remain binding.
Current code may need correction; it does not outrank the approved target merely
because tests describe its old behavior. Report a real contradiction explicitly.

Preserve these original PNGs. Do not replace them with implementation screenshots,
new AI interpretations, cropped variants or the older concepts. A design change
needs explicit owner approval, a new versioned reference and a recorded delta.
Accessibility fixes and the semantic corrections below are already authorized.

## Screen map

| Screen ID | Reference | Required structure |
|---|---|---|
| `signaux` | [01-signaux.png](prototypes/2026-09-28/01-signaux.png) | Session strip; optional urgent band; 2–3 compact signal cards; in-progress/waiting summary; bottom analysis status |
| `plan-comparison` | [02-plan-comparison.png](prototypes/2026-09-28/02-plan-comparison.png) | Alternative plan rows; highlighted recommendation; persistent right recap with grouped phases and Start |
| `active-plan` | [03-active-plan.png](prototypes/2026-09-28/03-active-plan.png) | Grouped vertical phases left; exact current instruction center; plan recap and next step right |
| `session-preferences` | [04-session-preferences.png](prototypes/2026-09-28/04-session-preferences.png) | Right drawer with dimmed backdrop; session controls, advice, Apply and save-default actions |
| `bilan` | [05-bilan.png](prototypes/2026-09-28/05-bilan.png) | Four distinct result metrics; plan outcome table; time/feedback panel; explanation disclosure |
| `settings` | [06-settings.png](prototypes/2026-09-28/06-settings.png) | Settings tabs; protections and account coverage; native-notification readiness/test; local data |

The [manifest](prototypes/2026-09-28/manifest.json) records SHA-256 and native
sizes. Originals are **1672×941**, approximately 16:9; the implementation target
is a **1920×1080 CSS-pixel viewport**, Windows landscape at 100% browser zoom.
Also verify Windows scaling/zoom accessibility; never shrink all text just to
fit a screenshot. The CI integrity check protects reference bytes, not UI fidelity.

## Shared visual language

- Deep blue/near-black background; slightly raised navy panels with thin muted
  borders. Restrained gold for primary actions, selected navigation and emphasis.
- Large white page titles, muted cool secondary text, readable metric values.
  Keep the restrained fantasy identity and TL/wordmark treatment. Use real
  text/vector assets and a coherent icon set; do not ship screenshot text as UI.
- Left rail spans the viewport height, roughly 15% of width (about 280 px at
  target). `Signaux`, `Plans`, `Bilan`; `Réglages` anchored at the bottom.
  Keep one stable shell/icon vocabulary across all screens despite image variants.
- Main content starts around 16% of width, with about 24–32 px gutters and
  16–24 px panel gaps at target. Match the reference proportions before detail.
- Bottom analysis status remains in the same shell position. It reflects actual
  local/background state; a green dot requires supporting evidence.
- Comparison uses roughly 70/30 main/recap width. Execution uses approximately
  24/47/29 for phases/instruction/recap within the content region. The session
  drawer uses roughly 36% of the viewport. These are implementation starting
  proportions, not fixed-width constraints that override readable text.
- Preserve compact density: key controls, 2–3 signals or comparison choices and
  background status visible without a long page scroll at the target viewport.
  Long plans/settings can scroll within a clear region; do not force every step
  into a horizontal timeline. Primary current instruction stays easy to find.
- Gold means action/selection, amber means uncertainty or urgent attention, green
  means evidenced healthy/successful state. Never communicate by color alone.

## Mandatory corrections to the generated mockups

| Image detail | Implementation contract |
|---|---|
| 01 asks the player to choose compatible resources | The engine enforces compatibility and explains any constraint. The user chooses a plan, not conflict resolution. |
| 01 has an urgent band | Render it only for a materially urgent actionable condition. No permanent warning banner or silence/game-mode toggle. |
| 02 says three compatible plans | These are alternatives compatible with the session, not necessarily simultaneously affordable plans. Preview reserves nothing. Start revalidates/reserves atomically. |
| 03 lacks the later-approved fallback button | Add `J’ai effectué cette étape` as an exceptional pending-API action. No quantity or price form. Display `Déclaré effectué · vérification en attente` after reporting. |
| 03 says next step waits for verification | Permit safe provisional continuation. Block only dependencies that truly need authoritative evidence; show the reason and waiting time. |
| 03 green materials/protection badges | Show only with adequate actual coverage. Unknown equipment/inventory evidence is not permission to consume it. |
| 04 background uses a different feed and misleading surplus profit | Use **01** for the background. **04 is authoritative for the drawer only.** Cash released is not profit when basis is unknown. |
| 04 shows only active time and 30 min selected | Add both `Temps pour agir` and `Or disponible avant…` objectives. Initial suggestion is 15 min; 30 min is merely an illustrated selection. |
| 04 includes unimplemented activities | Only supported verified strategies are selectable. Others are labelled unavailable. Do not fake implemented event/conversion/salvage engines. |
| 04 condensed risk row | Keep allocation, untouched reserve, downside tolerance and lock horizon distinct. Explain defaults; no silent relaxation. |
| 05 time and profit totals | Time is estimated unless measured; only evidenced costs/fees allow verified profit. Do not add cash released or unrealized gains to realized profit. |
| 06 claims complete account coverage | Show per-source/character actual coverage and missing permissions. No fabricated all-characters/notification success state. |
| Any mock value, character, item, logo variant or exact wording | Illustrative. Use backend evidence, French approved copy and consistent assets; never hardcode the screenshot's numbers into live state. |

## Required implementation and review evidence

Before a UI ticket starts, name its screen IDs and open their PNGs plus this
contract. Non-UI tickets need not load all images. Missing image-view capability
blocks a visual-fidelity verdict; it does not authorize an unreviewed redesign.

Each UI PR must include:

1. Actual rendered application screenshots at **1920×1080**, with viewport,
   device scale, route, fixture/state and commit SHA recorded. Include the shared
   shell and each changed screen. Use deterministic non-private fixtures.
2. Side-by-side baseline/actual evidence or a clear paired image list, and a
   short delta table: hierarchy, layout proportions, density, color/type, controls
   and states. Semantic corrections above must be named, not reported as drift.
3. Functional/accessibility evidence: keyboard flow, visible focus, drawer focus
   trapping/restoration, Escape/cancel, contrast, French names and no clipping at
   supported zoom. A screenshot cannot prove these behaviors.
4. A fresh reviewer opening both actual screenshots and original references.
   Missing screenshots, unreadable core content, material layout drift, wrong
   navigation or invented interactions are **Important** findings; keep Draft.
5. Owner-visible preview at the P05 checkpoint. Do not declare owner acceptance
   from automated screenshot checks or the author's self-review.

Use stable visual regression baselines from the *implemented* UI after its first
approved checkpoint. Generated images guide fidelity; antialiasing and generated
text make raw pixel equality against them inappropriate. Screenshot updates must
be reviewed, never blindly accepted after a failure.

## Required state coverage

Screenshots must cover changed states, with the UI package eventually covering:
normal 2–3 signals, no qualifying signal, urgent action, stale/partial account,
no plan for the selected objective, insufficient capital, preview vs started
plan, pending local report, safe continuation, material contradiction/paused
plan, long grouped plan, unknown cost basis, and denied notification permission.
No-data/error states reuse the approved shell instead of creating a new design.
