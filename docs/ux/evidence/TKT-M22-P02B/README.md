# P02B — Legacy Crafting State Evidence

The independent NORMAL GPT-6.1 Sol High review identified a material rendering
transition through the existing `/api/crafting-opportunities` consumer, despite
no frontend source change. These actual application captures resolve that missing
evidence. They are synthetic fixtures, not authenticated account acceptance or
owner UI acceptance, and authorize no replacement design.

Runtime/source head: `4f71aa25414a94fd9737e1483bac84019e643691`.
Captured with uncommitted evidence-only tests/fixtures; no runtime/frontend edits
followed that head. [Manifest](manifest.json) records dimensions, hashes, viewport,
route and fixture boundary. Chromium, 1920×1080 CSS pixels, device scale 1,
100% capture zoom; a separate 125% zoom assertion passes with no horizontal clipping.
Route: `http://127.0.0.1:5081/`, keyboard-select **Artisanat**.
The local host uses Testing, an empty credential and disposable test SQLite.

## Paired references and actual renders

- Shared shell authority: [original 01 Signaux](../../prototypes/2026-09-28/01-signaux.png).
- Plan visual context, opened without claiming this capture covers its comparison
  screen: [original 02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png).
- [Rejected mixed tuples](rejected-mixed-1920x1080.png) — inactive 500 on one actor
  cannot inflate another actor's active 100 for a minimum-400 recipe.
- [Rejected inactive tuple](rejected-inactive-1920x1080.png) — inactive 500 alone
  cannot create an executable craft.
- [Eligible single actor](eligible-single-actor-1920x1080.png) — active 500 on a
  real actor retains the existing card and Start affordance.

The response fixtures are produced by the real planner and production response
mapper in `CraftingOpportunityRenderingEvidenceTests`, rather than a frontend
reimplementation. The mixed active-100 summary is independently anchored by the
Infrastructure gateway regression. They are intercepted only at the browser's
local crafting-response boundary. Other local endpoints run normally with no key.
The Start control is observed/focused, never invoked; no stock is reserved.

| Comparison dimension | Actual scoped result / retained limitation |
|---|---|
| Hierarchy and state | False eligible card is absent; French empty-state heading and refresh control are readable. A valid actor retains the existing opportunity card. No invented completeness badge. |
| Layout and density | All changed content is visible at 1920×1080. The legacy shell has a 240px rail and wider gutters; the approved target has about 280px rail and denser session/feed structure. No layout was changed in P02B. |
| Color and type | Legacy green-black background and serif headings remain; original 01 specifies navy and large white sans-like headings. Gold controls/focus remain visible. These are inherited shell differences, not a new visual exception or claimed fidelity approval. |
| Navigation and controls | Existing legacy Mes Signaux / Plans / Artisanat / Réglages remains, versus approved Signaux / Plans / Bilan with bottom Réglages. P05 fixture previews are separate; live shell convergence remains later UI work. P02B adds no navigation or controls. |
| Changed semantics | Capability eligibility now uses active tuples from one real actor. No economic guarantee, spendable protected stock or new actor-specific instruction is shown. Existing explanatory copy is retained. |
| Functional/accessibility | Keyboard Enter activates Artisanat; affected refresh/Start controls can receive visible focus. Main-region axe checks pass for all three states; no horizontal clipping at 100% or 125%. No drawer/focus-trap behavior changes. |

No runtime layout/text/UI redesign is requested by this correction. This evidence
reviews the changed state transition in the current legacy application, not
owner acceptance of the old shell or completion of P05/live UI integration.

## Reproduction and preview

From the repository root, export backend-generated safe fixtures:

```sh
TYRIAN_LEDGER_P02B_VISUAL_DIR="$PWD/docs/ux/evidence/TKT-M22-P02B/fixtures" dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj --filter FullyQualifiedName~CraftingOpportunityRenderingEvidenceTests --verbosity quiet
```

With locked frontend/E2E dependencies and Chromium installed, run from
`tests/Gw2Tp.Web.E2E`:

```sh
TYRIAN_LEDGER_P02B_SCREENSHOT_DIR="$PWD/../../docs/ux/evidence/TKT-M22-P02B" npx playwright test tests/p02b-crafting-policy.spec.ts --project=chromium
```

The ordinary browser test runs assert these states without rewriting PNGs. The
optional screenshot variable explicitly enables capture. The attached PNG links
are the owner-visible preview; no public deployment is needed. Three Chromium
render tests passed; the final PR CI covers the existing browser matrix.
