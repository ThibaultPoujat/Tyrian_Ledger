# P02C — Protected Holdings Rendering Evidence

Runtime/source head: `4285d4b1cd87388b552cffc1f6d67717255b5afa`.
The seven actual 1920×1080 application screenshots use Chromium, device scale 1,
100% capture zoom, the Testing loopback host with an empty credential and disposable
SQLite, and the existing production React consumers/response mappers. The
[manifest](manifest.json) records dimensions, hashes, route/state and fixture boundary.
This is synthetic local integration evidence, not authenticated ArenaNet transfer
proof, Windows performance acceptance or owner acceptance of the legacy shell.

## Reference comparison

Opened originals: [01 Signaux](../../prototypes/2026-09-28/01-signaux.png),
[02 comparison](../../prototypes/2026-09-28/02-plan-comparison.png),
[03 active plan](../../prototypes/2026-09-28/03-active-plan.png).
Their source raster is 1672×941; actual application captures are 1920×1080.

| State / actual capture | Scoped behavior |
|---|---|
| [Bank input](bank-1920x1080.png) | One eligible location, required actual actor and French access explanation; Start is visible/focused. |
| [Own bag](own-bag-1920x1080.png) | The same projection authorizes only the capable character's own inventory. |
| [Partial bank, safe material source](partial-bank-1920x1080.png) | Bank permission failure cannot erase independently eligible material evidence; no Complete coherence claim. |
| [Other character bag](other-bag-1920x1080.png) | Unsupported transfer excludes the opportunity; no Start/consuming instruction. |
| [Other character bound input](bound-other-1920x1080.png) | Actor/binding mismatch excludes consumption. |
| [Protected input](protected-1920x1080.png) | Equipment protection excludes consumption with a French prerequisite explanation. |
| [Moved input pause](paused-location-1920x1080.png) | The committed bank input moved to materials; the plan retains its original commitment and shows French verification text without a completion control. |

| Comparison dimension | Result / retained limitation |
|---|---|
| Hierarchy and semantics | Added actor/access prerequisites appear beside the existing craft economics. Blocked states replace executable cards; a moved plan has no current consuming control. Copy is French and does not promise coherent or confirmed stock. |
| Layout and density | All affected content fits the target viewport. No styles/layout/navigation changed. The legacy 240px rail and single wide cards differ from the approved approximately 280px rail, session strip, compact comparison rows and grouped three-column execution. |
| Color and typography | Existing green-black background and serif headings differ from the approved navy and white sans-like headings. Gold/focus remain readable. These inherited differences are recorded, not claimed as new visual approval. |
| Navigation / scope | Legacy Mes Signaux / Plans / Artisanat / Réglages remains versus approved Signaux / Plans / Bilan and bottom Réglages. P05C live-screen wiring and shell convergence are outside P02C; preview providers are unchanged. |
| Interaction / accessibility | Keyboard Enter selects the section; eligible Start is focusable but never invoked. Each affected main region passes axe, and 100%/125% zoom has no horizontal clipping. No new drawer, control, execution form or layout was introduced. |

Application `CraftingOpportunityServiceTests` generates the six crafting domain
vectors through the live service, actual policy/projector and real deterministic
planner. `HoldingsRenderingEvidenceTests` uses the production safe mapper to export
their browser fixtures and independently creates the moved-input plan through real
admission/orchestration/revalidation. Browser interception occurs only at the local
crafting or plans JSON boundary; other endpoints run on the actual empty-key host.
The browser test does not reproduce financial/protection formulas or place orders.
The public duplication test was run before the old endpoint adapter changed and
failed; with normalized holdings it now asserts exactly 10, rather than 20, for
portfolio 10 + bank 10. Domain tests separately verify 6+4 caps at 6, keep 150−100=50,
delivery zero, provisional output waits and per-source Partial physical evidence.

## Reproduction and preview

From the repository root (locked frontend/E2E dependencies and browser installed):

```sh
TYRIAN_LEDGER_P02C_DOMAIN_DIR=/private/tmp/tyrian-p02c-domain dotnet test tests/Gw2Tp.Application.Tests/Gw2Tp.Application.Tests.csproj -c Release --filter FullyQualifiedName~CraftingOpportunityServiceTests
TYRIAN_LEDGER_P02C_DOMAIN_DIR=/private/tmp/tyrian-p02c-domain TYRIAN_LEDGER_P02C_VISUAL_DIR="$PWD/docs/ux/evidence/TKT-M22-P02C/fixtures" dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj -c Release --filter FullyQualifiedName~HoldingsRenderingEvidenceTests
cd tests/Gw2Tp.Web.E2E
TYRIAN_LEDGER_P02C_SCREENSHOT_DIR="$PWD/../../docs/ux/evidence/TKT-M22-P02C" npx playwright test tests/p02c-holdings.spec.ts --project chromium
```

Open a linked PNG for review or run the browser test with `--headed` to preview.
It opens `http://127.0.0.1:5081/`, selects Artisanat/Plans with the keyboard and
supplies the committed safe fixture. It also starts the isolated P05 preview
server at 5181 without changing its providers or asserting P05C integration.
