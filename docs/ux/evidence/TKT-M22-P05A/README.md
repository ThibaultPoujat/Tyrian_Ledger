# P05A preview and visual comparison

The screenshots below were captured from the shared React experience in commit
`3a69f9369dee4a734e93f7a2a5a885f66fad475a` using the `p05a-preview` Playwright
project. Each actual image is **1920×1080 CSS pixels** at `deviceScaleFactor: 1`
(100% browser zoom). Routes use `preview.html?scenario=normal`, `urgent`, or
`degraded`; the drawer capture opens `Adapter ma session` from the normal route.
The fixture provider is deterministic and makes no API or upstream requests.

| State | Approved original | Actual rendered screenshot | Comparison |
|---|---|---|---|
| Normal, three Signals | [01-signaux.png](../../prototypes/2026-09-28/01-signaux.png) | [signaux-normal-1920x1080.png](actual/signaux-normal-1920x1080.png) | Same navy shell, ~15% rail, session strip, three compact cards, in-progress/waiting rows and bottom analysis bar. The urgent band is omitted in this normal fixture, as required. |
| Urgent action | [01-signaux.png](../../prototypes/2026-09-28/01-signaux.png) | [signaux-urgent-1920x1080.png](actual/signaux-urgent-1920x1080.png) | The amber band uses the reference hierarchy and appears only for this urgent fixture; the same three-card layout remains visible. |
| No qualifying Signal, partial coverage | [01-signaux.png](../../prototypes/2026-09-28/01-signaux.png) | [signaux-vide-couverture-partielle-1920x1080.png](actual/signaux-vide-couverture-partielle-1920x1080.png) | Keeps the same shell and card region, replacing cards with an honest empty state, missing-coverage explanation and partial status. No unsupported green coverage badge appears. |
| Session drawer, objectives and allocation | [04-session-preferences.png](../../prototypes/2026-09-28/04-session-preferences.png) (drawer authority; [01-signaux.png](../../prototypes/2026-09-28/01-signaux.png) supplies the background) | [session-drawer-1920x1080.png](actual/session-drawer-1920x1080.png) | Right drawer is about 36% of the viewport over the dimmed Signaux screen. Sections remain scrollable above persistent actions, with objectives, provider-owned allocation, thresholds, supported and unavailable activities separated. |
| Session drawer, reserve and risk | [04-session-preferences.png](../../prototypes/2026-09-28/04-session-preferences.png) (drawer authority; [01-signaux.png](../../prototypes/2026-09-28/01-signaux.png) supplies the background) | [session-drawer-risques-1920x1080.png](actual/session-drawer-risques-1920x1080.png) | Shows distinct reserve, downside and lock-horizon controls with their French guidance; the downside value explicitly does not guarantee a maximum loss. |

## Visual delta table

| Area | Reference comparison |
|---|---|
| Hierarchy | Large page title, session strip, Signal heading/cards, in-progress and waiting summaries, and bottom analysis status stay in the same order. The empty state gives the main panel to coverage guidance. |
| Proportions | The rail is 280/1920 px (14.6%), close to the approved ~15%; the drawer is 690/1920 px (35.9%), close to the approved ~36%. |
| Density | Three cards fit in one row at the target viewport. Omitting the optional urgent band in the normal fixture moves later content upward and leaves more open space below; the urgent fixture restores that band and its visual hierarchy. |
| Color and type | Deep navy panels and muted borders remain; gold marks actions/selections, amber marks urgency or uncertainty. Page and drawer headings use the reference sans-serif treatment. Coverage text is paired with a `FICTIF` label and never uses an unsupported green success state. |
| Controls and states | The drawer keeps its apply/cancel actions visible while its body scrolls; unavailable strategies are disabled. Browser validation also checks the drawer at 1280×720 (a 150%-zoom-like CSS viewport) with the risk fields and footer actions reachable and no horizontal overflow. |

## Authorized semantic changes

- `01` shows an urgent band in its single illustration; P05A renders it only in the urgent fixture.
- The drawer supports both `Temps pour agir` and `Or disponible avant…`, starts at 15 minutes, and labels the saved default as an in-memory demonstration.
- Cash recoverable is shown separately from profit when cost basis is unknown. No future uncertain sale is described as already available gold.
- Demo coverage and fictional figures are labelled. Unsupported conversions, salvage and events remain disabled and marked unavailable; the preview never claims live readiness.
- The UI enforces resource compatibility in its provider contract and does not ask the player to resolve conflicts.
- The normal state intentionally has more open space below its summary after omitting the optional urgent band; no placeholder warning is used to fill it.

## Preview launch

From the repository root, install the frontend dependencies once and launch the preview:

```sh
npm --prefix frontend ci
npm --prefix frontend run preview:p05a -- --port 5181
```

Open `http://127.0.0.1:5181/preview.html?scenario=normal`. Replace `normal`
with `urgent` or `degraded` to inspect the other deterministic fixture states.
The preview is bound to loopback and is separate from the production entry. It
is not a live-engine fallback.

The focused Playwright run also captures these files:

```sh
TYRIAN_LEDGER_P05A_EVIDENCE_DIR="$PWD/docs/ux/evidence/TKT-M22-P05A/actual" \
  npm --prefix tests/Gw2Tp.Web.E2E test -- --project=p05a-preview tests/p05a-preview.spec.ts
```
