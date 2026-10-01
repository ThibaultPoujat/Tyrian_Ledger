# P05B — combined preview and visual evidence

This extends P05A's shared React shell, session drawer and fixture provider. It is
an explicit in-memory demonstration, isolated from the production build. It has
no live API adapter, game automation, financial policy, settlement or persistence.
Bilan and Réglages remain P05A placeholders. Owner acceptance is still required
at the B1 checkpoint after merge; screenshots do not constitute that acceptance.

## Capture identity

The actual PNGs were captured from implementation commit
`ddbbf66a1e762d29a635fceb81b230696f5cc132` by the `p05b-preview` Playwright project.
Every capture uses Chromium, **1920×1080 CSS pixels**, `deviceScaleFactor: 1`,
100% browser zoom, and `fullPage: false`. The route is
`/preview.html?scenario=<scenario>` as listed below. The originals remain intact.
The following evidence-only commit adds these images and this document without
changing the rendered implementation.

## Paired reference and actual images

| Scenario | Approved original | Actual rendered image | State evidence |
|---|---|---|---|
| `comparison` | [02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png) | [Comparison](actual/comparison-1920x1080.png) | Three alternatives, recommendation, session capital, persistent recap and one explicit Start. |
| `comparison`, select Ventes immédiates | [02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png) | [Alternate selected](actual/alternate-selected-1920x1080.png) | Selection updates recap and phases without starting; cash released and unknown profit are separate. |
| `no-plan` | [02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png) | [No qualifying plan](actual/no-plan-1920x1080.png) | Preferences stay intact, no Start, route back to the session drawer. |
| `insufficient-capital` | [02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png) | [Insufficient capital](actual/insufficient-capital-1920x1080.png) | A prepared rejection reason; no new execution or arbitrary relaxation. |
| `stale` | [02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png) | [Stale evidence](actual/stale-1920x1080.png) | Missing/old coverage blocks Start and remains amber in the shared status bar. |
| `active` | [03 execution](../../prototypes/2026-09-28/03-active-plan.png) | [Normal active plan](actual/active-1920x1080.png) | Grouped phases, focused exact current instruction, actor/location/resources, exceptional report. |
| `provisional` | [03 execution](../../prototypes/2026-09-28/03-active-plan.png) | [Provisional safe continuation](actual/provisional-1920x1080.png) | Prior step remains declared and pending; next instruction is provider-authorized without a fixed delay. |
| `wait` | [03 execution](../../prototypes/2026-09-28/03-active-plan.png) | [Mandatory wait](actual/wait-1920x1080.png) | Required proof, honest next-read estimate without a freshness guarantee, execution disabled. |
| `partial` | [03 execution](../../prototypes/2026-09-28/03-active-plan.png) | [Partial evidence / recheck](actual/partial-1920x1080.png) | 20 of 50 observed, 30 uncertain, no continuation, incomplete protections. |
| `contradiction` | [03 execution](../../prototypes/2026-09-28/03-active-plan.png) | [Contradiction](actual/contradiction-1920x1080.png) | Suspended guidance and recovery, no supported green protection claim. |
| `long` | [03 execution](../../prototypes/2026-09-28/03-active-plan.png) | [Long grouped plan](actual/long-1920x1080.png) | 24 deterministic steps in four collapsible groups; current step 14 remains visible and focused. |

All screens reuse the shell from [01 Signaux](../../prototypes/2026-09-28/01-signaux.png).
The combined flow also reuses the drawer from
[04 session preferences](../../prototypes/2026-09-28/04-session-preferences.png).
P05A's [paired evidence](../TKT-M22-P05A/README.md) supplies the earlier screen
baseline; its six browser tests are rerun with P05B to check shared-shell changes.

## Reference comparison and authorized semantic deltas

| Area | Comparison |
|---|---|
| Hierarchy | 02: session and capital above compact alternatives, gold recommendation/selection, persistent recap, grouped phases and Start. 03: phase list left, exact instruction center, economic recap and next-step context right. |
| Proportions | Same 280 px rail at 1920 px (~14.6%); comparison ~70/30; execution ~24/47/29, excluding gutters. Shared bottom analysis bar stays visible, and Réglages is anchored at the bottom. |
| Density | Three comparison choices and Start fit at target size. Long phases scroll within a named region. Longer recovery copy can scroll in the main region while rail/status remain fixed; current instructions and primary controls remain visible. |
| Color/type | Shared deep navy, muted borders, gold action/selection and readable white headings. Amber means uncertainty; incomplete protection coverage has no green badge. Same text/vector icon vocabulary, with decorative vector material/coin tiles rather than raster mockup text. |
| Controls/states | Real read-only text, French accessible buttons, copy-name action, keyboard choice/start, current-item focus, provider-owned eligibility and explicit recovery. No quantity or price form. |

Authorized corrections to 02/03 are deliberate: plans are alternatives, not
simultaneously affordable commitments; Start is explicitly simulated; uncertain
sale proceeds never qualify as deadline cash; unknown cost basis is not free.
03 gains the approved exceptional `J’ai effectué cette étape` action and pending
label, supports safe continuation, and blocks only provider-required evidence.
Cash released, modeled gain, unestablished realized profit, estimated active time
and waiting remain separate. Fixture coverage is always labelled as fictitious.
Pausing/Undo explicitly affect local guidance/declarations, never in-game orders.

## Owner preview

From the repository root:

```sh
npm --prefix frontend ci
npm --prefix frontend run preview:p05a -- --port 5186 --strictPort
```

Open `http://127.0.0.1:5186/preview.html?scenario=normal` for the combined flow:
Signaux → Adapter ma session → Apply → Voir le plan / Voir le détail → compare
alternatives → Démarrer ce plan → inspect the current instruction. Actualiser
l’observation supplies the next canned API-observed result. The exceptional
report offers safe provisional continuation; Pause, Resume, Undo and Help have
explicit deterministic outcomes. No action sends a production request.

Use `?scenario=comparison` for direct comparison or any scenario in the table
for a direct execution/recovery screen. `lost-ack` demonstrates a lost response:
report once, then use explicit Actualiser to check account scope and retry the
original intent. `conflict` demonstrates a rejected Start. Under Compte de
démonstration, changing the fixture account invalidates old pending intents.
Reload resets all fixture state. No browser or server persistence is claimed.

## Validation and review

```sh
npm --prefix frontend test
npm --prefix frontend run build
TYRIAN_LEDGER_P05B_EVIDENCE_DIR="$PWD/docs/ux/evidence/TKT-M22-P05B/actual" \
  npm --prefix tests/Gw2Tp.Web.E2E test -- --project=p05a-preview --project=p05b-preview \
  tests/p05a-preview.spec.ts tests/p05b-preview.spec.ts
node --test .github/scripts/*.test.mjs
```

Frontend tests cover duplicate in-flight Start/report, original execution/step/
revision identity, retry ordering after account-scope read, delayed old-account
acknowledgements, historical receipt replay, distinct new-step commands, normal
API-observed completion, eligibility, pause/resume and Undo. Browser tests check
the four-screen flow, clipboard, French names, visible focus, axe accessibility,
long-group scrolling, disabled actions, zero production/upstream requests and
1280×720 CSS viewport readability (150%-zoom-like layout, not a native Windows
scaling claim). The production build's isolation guard excludes the preview.

The scoped `p05b-preview` project is included in required CI's default browser
run alongside P05A and the production browser matrix. Actual final-head CI and
independent NORMAL Sol Medium review evidence are recorded in the PR and the
assigned #161 batch row. Missing evidence keeps the PR Draft. VERIFY-008 and
VERIFY-013 remain OPEN; this preview asserts no new upstream fact.
