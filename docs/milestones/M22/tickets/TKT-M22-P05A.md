# TKT-M22-P05A — Approved Shell, Signaux and Session Drawer with Fixtures

GitHub issue: #160

Milestone M22. Risk R1. Luna High implementation; NORMAL independent Luna High
review using tyrian-pr-review, including opening reference and actual images.
Batch B1, third implementation. Ready after P01D / #159 and C02 / #157 merge.
Its technical UI dependency is the approved baseline; sequence remains deliberate.

## Outcome / boundaries

The owner can use the approved Signaux screen and customize a simulated session
at 1920×1080. Build reusable application React components, not a second disposable
prototype. Live engine connection is a later ticket; do not fake live readiness.

Use a separate explicit development/test preview entry that renders those shared
components through typed fixture providers. It must carry a persistent French
"Démonstration · données fictives" indicator, make no /api mutation or ArenaNet
request, and never activate as a fallback when the real API fails. Keep the
normal production entry on its existing live behavior until integration.
No backend changes, real credentials, financial calculation engine, notifications,
production fixture defaults or new external dependency without its normal approval.

## Read / edit entry points

- docs/ux/tyrian-ledger-visual-reference.md and approved product direction
- OPEN docs/ux/prototypes/2026-09-28/01-signaux.png
- OPEN docs/ux/prototypes/2026-09-28/04-session-preferences.png (drawer only)
- frontend/src/App.tsx, App.css, MoneyDisplay.tsx, existing tests and build setup
- reusable frontend/src components plus a small explicit preview entry/provider
- tests/Gw2Tp.Web.E2E Playwright setup; extend with a scoped preview project as needed

The six original PNGs stay byte-identical. The old Sites prototype and earlier
concepts are not alternatives. Do not ship image text as interface controls.

## Display and interactions

1. Stable navy/gold shell, roughly 15% left rail, Signaux / Plans / Bilan with
   Réglages anchored low, bottom analysis status. Match 01 proportions, spacing,
   typography and compact density. No game/silent mode switch.
2. Show session strip and 2–3 compact cards with distinct estimated net profit,
   cash released, active time and capital commitment. An urgent band appears
   only in the urgent fixture. Compatibility belongs to the engine/provider;
   never ask the user to solve resource conflicts.
3. Drawer follows 04, over the 01 background. Offer both Temps pour agir and
   Or disponible avant…; 5/15/30/custom minutes; allocation 15/30/50/custom percent;
   minimum gain/plan and gain/active minute; supported activities; separate
   untouched reserve, downside tolerance and lock horizon fields with explanation.
   Starting suggestions: 15 minutes, 30%, 2 po/plan, 0.5 po/active minute.
   Preserve existing accepted semantics; do not invent a guaranteed return.
4. The allocation summary receives base/ceiling/commitments/remaining amounts
   from the fixture provider. React must not define new financial authority.
   Advice is explicit text, never silent threshold relaxation. Unsupported
   conversions/salvage/events are labelled unavailable, not working strategies.
5. Edit a draft; Apply changes preview session only; Cancel/Escape leaves it
   unchanged. Save-default acts on a distinct in-memory fixture-default store
   and is clearly simulated. No production setting persistence is claimed.
   Validate positive time, percentages within 0–100 and nonnegative monetary
   thresholds; use integer-copper strings at the provider boundary.
6. Until P05B lands, card/Plans actions reach a labelled preview placeholder with
   a back path. Bilan/Réglages also reach honest placeholders; no invented screen
   redesign or dead controls. Existing live routes remain reachable normally.

## Typed handoff for P05B and live integration

Expose view models for SessionPreferences, SignalCard, AccountCoverage,
AnalysisStatus and action callbacks. Use structured certainty/eligibility/reason
states and Money copper strings. The preview provider owns scenario changes;
components render data and user intents. Document which values will come from
live services; fixture interfaces are presentation contracts, not new engine truth.
Use local existing assets/icons where possible; no new image generation is needed.

## Acceptance / evidence

- Normal 3 cards, no qualifying signal, urgent action, stale/partial account and
  drawer-open states render coherently; no unsupported green coverage badge.
- Changing objective/parameters switches deterministic fixture results; session
  cancel/save-default semantics are separately tested.
- Keyboard open/close, focus trap/restoration, Escape, visible focus, French
  accessible names; reasonable zoom does not hide essential controls.
- Actual screenshots at 1920×1080 for normal, urgent, empty/degraded and drawer.
  Record route/scenario, viewport/device scale and commit SHA; pair with 01/04
  and note authorized semantic corrections. Reviewer opens both sets.
- Network test proves preview emits no API mutations and no upstream calls.
  Production build cannot silently enter fixture mode on host failure.
- Owner can launch the preview using one documented command and clear route.
  AI provides evidence/launch instructions; owner need not edit code.

Run npm --prefix frontend test, npm --prefix frontend run build,
npm --prefix tests/Gw2Tp.Web.E2E test -- <scoped-preview-spec>, node --test .github/scripts/*.test.mjs,
and required CI. Ensure the preview project runs automatically in required CI,
not only manually. Capture screenshots from actual rendered code, not imagegen.
No fidelity approval without image-view capability/evidence; keep Draft.

Prefix [TKT-M22-P05A], PR to develop, Closes #160, milestone 11.
Record batch evidence and preview instructions. Stop for owner merge.
Next prepared ticket: P05B; no Astra checkpoint between these screens.
