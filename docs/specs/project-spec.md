# Project Specification - Tyrian Ledger

## 1. Product statement

Tyrian Ledger is a static, browser-based decision-support tool for Guild Wars
2 Trading Post analysis. It helps users assess potentially profitable manual
in-game actions from a periodically generated public market snapshot.

It is not a trading bot, gameplay bot, order executor, browser automator, or
autonomous game agent.

## 2. Product principles

1. **Read-only by design.** Only the scheduled generator requests permitted
   public Guild Wars 2 resources through the typed GET-only gateway. The
   browser makes neither Guild Wars 2 nor local API requests.
2. **Deterministic financial truth.** C# calculations use integer copper and
   browser recommendation calculations use `BigInt`; neither is delegated to
   an LLM.
3. **Data freshness is first-class.** The browser shows snapshot generation
   time and suppresses recommendations when the snapshot is delayed,
   incompatible, malformed, or unavailable.
4. **Explicit assumptions.** A recommendation is a scenario, not a guarantee
   of execution or profit.
5. **Privacy by minimization.** The static site has no account connection,
   credential store, server-side preference profile, or database. Capital and
   risk settings remain browser-local.
6. **Testability.** Core calculations, snapshot contracts, gateway behavior,
   generator output, and static browser behavior are independently tested.

## 3. Scope

### Current scope

- bounded collection of public prices, listings, and item metadata through the
  typed gateway;
- a complete versioned `market-snapshot.json` artifact;
- deterministic fee-aware flipping scenarios and order-book simulation;
- browser-local capital and risk preferences with `BigInt` recommendations;
- data freshness, delayed-refresh, malformed, incompatible, and unavailable
  snapshot states;
- static React assets, browser accessibility coverage, and fixture-based test
  suites.

### Explicitly out of scope

- Trading Post actions or gameplay automation;
- browser automation against the game or Trading Post;
- browser-side Guild Wars 2 or local API access;
- API keys, account data, crafting, personal history, authentication, or cloud
  persistence;
- a hosted dynamic API or database;
- an application LLM;
- prediction, execution-time guarantees, or a guarantee of profit.

## 4. Users and main journey

The initial users are the owner and invited testers of the static site.

1. The browser loads the current static snapshot and displays its generation
   time and actionable state.
2. The user enters capital and chooses a risk profile; both values remain in
   that browser only.
3. The browser calculates and ranks deterministic recommendations locally.
4. The user reviews modeled costs, fees, return, liquidity assumptions, and
   freshness before taking any manual in-game action.

## 5. Non-functional requirements

- The site must build and preview as static files with no local ASP.NET host.
- No credential, account data, or player identifier may enter source,
  workflows, snapshots, browser storage, logs, or fixtures.
- Normal tests must use fixtures and mocks rather than live Guild Wars 2
  requests.
- UI must support current desktop Chrome, Firefox, and Safari; the automated
  matrix uses Playwright Chromium, Firefox, and WebKit.
- UI must support keyboard navigation, semantic controls, sensible focus
  management, and WCAG 2.2 AA contrast.
- No decorative reuse of Guild Wars 2 proprietary UI assets is permitted.

## 6. Market analysis requirements

The generator uses public aggregate prices for broad screening and complete
order books for finalists. The browser validates the snapshot contract before
converting safe JSON integers to `BigInt` and recalculating the M9 policy.

Fee rules remain centralized and modeled in whole copper. A market snapshot is
a point-in-time input: it is not a recommendation, financial guarantee, or
instruction to trade.

## 7. External verification policy

Exact endpoint schemas, quotas, cache guidance, and fee behavior are external
contracts. They must be tracked in the VERIFY register until supported by
authoritative evidence. The active public endpoints are prices, listings, and
items; all access uses the typed gateway and the documented read-only policy.

## 8. Completion standard

A release requires a fresh, valid static snapshot; a browser experience that
does not depend on local services; passing clean-checkout validation; no
tracked secret; reproducible calculations; and evidence that the read-only,
static boundaries are enforced.
