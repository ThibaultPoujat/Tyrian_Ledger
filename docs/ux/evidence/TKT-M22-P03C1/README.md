# P03C1 — Undo Transport Visual Evidence

Screen IDs: `active-plan` and shared `signaux` shell. Opened originals
[01](../../prototypes/2026-09-28/01-signaux.png) and
[03](../../prototypes/2026-09-28/03-active-plan.png).
Captured runtime head `ac73f75d898c03998e34e440e16ababda3886942`, `http://127.0.0.1:5081/`, Plans selected,
Chromium, 1920×1080 CSS pixels, device scale 1, 100% zoom, macOS ARM64.

| Actual image | Fixture/state |
|---|---|
| [Reported](reported-1920x1080.png) | Real production mapper/SQLite report; pending local event, focused French undo button. |
| [Undone](undone-1920x1080.png) | Real atomic undo/revalidated holdings response; reversed local event, current instruction restored. |

Fixtures are exported by the actual-host barrier test using
`PlanEndpointService.ToResponse` on real SQLite records with the actual host JSON options; all actors/items/account
values are synthetic. The browser intercepts only safe local Plans JSON and undo
response, asserts the outgoing view header/revision and invokes no game/API action.
Backend acceptance is established separately by actual-host/SQLite tests.

| Dimension | Comparison / scoped change |
|---|---|
| Hierarchy and semantics | French pending/undo/current instruction remain unchanged. Only revision/scope request transport changes; no new visible control or copy. |
| Proportions and density | Legacy 240px rail and wide single card remain versus approximately 280px and grouped three-column execution in 03. All scoped controls fit 1920×1080. |
| Colors and type | Existing green-black/serif shell remains versus approved navy/white/gold composition. No style changes. |
| Controls and approved corrections | Existing Terminé/quantity-price fallback remain legacy; approved exceptional exact-step report and grouped live surface remain P05C work. Undo reverses a local declaration only. |
| Navigation and shell | Inherited Mes Signaux / Plans / Artisanat / Réglages remains; approved Signaux / Plans / Bilan and bottom Réglages are still a known P05C gap. No new visual exception or owner acceptance is claimed. |
| Interaction/accessibility | Keyboard navigation, visible undo focus/Enter, one request with displayed revision/scope, axe main-region checks and no horizontal clipping at 100%/125% pass. No drawer/focus-trap change. |

Reproduce from repository root with locked dependencies and installed Chromium:

```sh
TYRIAN_LEDGER_LOCAL_COMMAND_FIXTURES="$PWD/docs/ux/evidence/TKT-M22-P03C1/fixtures" dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj -c Release --filter FullyQualifiedName~Actual_host_complete_and_undo
cd tests/Gw2Tp.Web.E2E
TYRIAN_LEDGER_P03C1_SCREENSHOT_DIR="$PWD/../../docs/ux/evidence/TKT-M22-P03C1" npx playwright test tests/p03c1-local-command.spec.ts --project chromium
```

Use `--headed` for preview. Review both originals and both actuals. Inherited
layout gaps are documented, not a fidelity approval or newly authorized redesign.
