# TKT-M22-S01 - Explain Calculations with Real Account Evidence and Decision Provenance

GitHub issue: #145

## Milestone

M22 - Continuous Operation, Hardening, and Evaluation

## Placement and development status

This implementation follows TKT-M22-01 / #95 and precedes TKT-M22-02 / #96. The planning/scaffold PR may be opened in advance, but implementation must incorporate #95's final merged contracts; rebase before implementation. Do not merge a docs-only Draft PR as though this feature were implemented.

## Implementation contract

## Goal
Make the local-first assistant's economic and eligibility decisions understandable and verifiable. Add **`Comprendre mes calculs`** under **`Réglages`**, paired with a contextual **`Pourquoi ?`** explanation on each Signal, Plan and eligible crafting path. Show both the versioned theory and the *actual inputs, intermediate results, constraints and provenance used for the current account/snapshot*. Do not create a second financial calculator in React.


## Functional scope
- Compact `Réglages > Comprendre mes calculs` with progressively disclosed sections for **capital/risk limits**, **Trading Post economics**, **crafting economics**, **Signals/Plans eligibility and selection**, and **evidence freshness/confidence**. English repository identifiers; all new user-facing text, errors and accessibility copy in French.
- Each section has two distinguishable modes:
  1. **Theory:** plain-language, versioned formulas and policies, denominators, rounding and important limitations, linked to the existing authoritative policy/VERIFY provenance.
  2. **My current calculation:** account-scoped read-only values with source and observation timestamps, known/unknown markers, policy version, intermediate arithmetic, existing exposures/reservations and precise binding constraints. A value absent or stale must be marked unknown/unavailable, never rendered as zero.
- **Capital:** verified wallet/available cash, individually scoped existing exposure, total bankroll denominator, the configured cash reserve and item/liquidity/strategy/category caps, after-reserve cash, competing reservations and the exact reason a candidate has zero safe capacity. Distinguish new-buy sizing from owned-inventory sale/list feasibility (including listing fees and unknown acquisition basis). Explain `buy_sizing_unavailable` using a typed cause rather than its raw code.
- **Trading:** acquisition basis and provenance, integer-copper gross/net sale, independently rounded canonical listing/exchange fees, minimums, full upfront cost, profit, ROI and break-even. Explicitly expose provisional fee-rounding assumptions from VERIFY; never promise realized profit.
- **Crafting:** verified owned quantities, missing input/price evidence, alternative procurement (owned at opportunity cost, instant buy, modeled buy orders, crafted intermediate), output liquidity, canonical fees, best realistic raw-input alternative and reasons such as `MissingInputEvidence`. Do not pretend a recipe is profitable with incomplete data.
- **Decisions:** on each `Pourquoi ?`, use the very same backend decision trace/snapshot used to generate its Signal or Plan. Show accepted vs excluded, eligible vs merely observed, candidates generated/filtered/resource-conflicted/selected, utility components where meaningful, relevant timestamps and the specific rule/constraint that changed the outcome. Distinguish stale-but-displayed cached results from currently executable actions.
- Provide typed, versioned, minimal **read-only loopback API** projections. No new ranking/economic formulas, trading automation, remote telemetry or browser-side secret storage. Account changes, refresh and Plan mutations must scope/invalidate displayed explanations consistently with the decision cache. Keep local execution shadow and verified ArenaNet evidence separate.
- Keep the approved compact second-screen layout, progressive disclosure, and existing four primary destinations; do not turn Settings into a trading terminal or expose raw private diagnostics by default.

## Acceptance criteria
- [ ] A user can reproduce the configured cash-reserve and item-cap numbers from a supported account snapshot, including the exact wallet/exposure denominator and rounding.
- [ ] Theory and displayed live calculations derive from canonical versioned backend policy, with no independently reimplemented React math; formula/policy changes cannot leave UI copy silently inaccurate.
- [ ] A visible Signal and its derived Plan show matching calculation lineage, eligibility and resource/exclusion reasons for the *same snapshot*. Distinguish non-executable Signals rather than suggesting action the Plan must block.
- [ ] Exact missing and stale inputs are shown for `buy_sizing_unavailable` and `MissingInputEvidence`, including whether another supported source/refresh can supply them; known vs unknown remains unambiguous.
- [ ] Listing/selling owned inventory does not inherit purchase-specific display explanations where those constraints do not actually apply.
- [ ] Trading and crafting examples cover canonical fees, integer-copper rounding, cost basis, opportunity cost, resource reservations, confidence and unknown/partial evidence.
- [ ] Scope changes, stale snapshots, refreshes, concurrent reads and Plan mutations cannot leak cross-account values or present stale explanations as executable.
- [ ] French UI, keyboard navigation, screen-reader disclosure, high-density 1920×1080 presentation and narrow viewport are covered; progressive detail does not block ordinary navigation.
- [ ] No credentials, authorization headers or sensitive diagnostic blobs in frontend payloads, logs, fixtures, screenshots or browser persistence.
- [ ] Real-backend deterministic integration and E2E tests cover accepted, rejected, degraded and no-opportunity flows; owner reviews real-application screenshots side-by-side with the approved visual direction.

## Dependencies
#95, #94, #133. Begin implementation after #95 merges or safely rebase on its final merged head. The current #144 Draft must remain independent.

## Non-goals
- Changing the canonical fee/risk/recommendation policy solely to make explanations look better.
- Inventing missing market/account evidence or promising profit/filled orders.
- Reintroducing an exhaustive scanner/dashboard as primary navigation.
- Full prototype redesign (separately planned visual-fidelity work), automatic strategy self-modification or cloud analytics.

## Review
**R3 NORMAL:** Terra High implementation and independent findings-first Terra High review/check in the same run when supported, with full cross-layer tests and CI. Escalate consequential unresolved financial/state-authority ambiguity to fresh Sol XHigh. Draft PR until implementation and owner acceptance. Owner merges only.

## Validation

- Contract tests: canonical integer-copper fees/rounding, policy version, wallet/exposure denominator, cash reserve, item/strategy/category/liquidity caps and known/unknown values match the existing engines.
- Read-only API integration: same snapshot/decision identity and rule version for a displayed Signal and Plan; cross-account scope, stale/expired data, concurrent refresh and invalidation after Plan mutation.
- Crafting integration: known owned quantities, unknown input opportunity costs, procurement alternatives, `MissingInputEvidence` and no invented gain.
- UI/Playwright: French theory versus real account values, compact `Réglages` disclosure, actionable `Pourquoi ?` deep links, rejected/empty/degraded states, keyboard and screen-reader behavior at 1920x1080 and narrow viewport.
- Security: secret scans and tests confirming the browser never receives API credentials or raw sensitive diagnostics; no browser durable storage of account evidence.
- Real-backend fixture demonstrating one supported trading Plan and one supported craft; real-account owner screenshots and truthful negative/exclusion behavior when available.
- Full affected .NET/frontend/E2E suites, independent Terra High findings-first review, required GitHub CI. The review follows `.codex/skills/tyrian-pr-review/SKILL.md`.

## Implementation guardrails

Explain the current backend authority; do not silently change policies or derive math from localized strings. Every displayed amount/ratio must carry typed provenance, configuration/rule version and appropriate observation time. Include truthful uncertainty about externally unverified fractional-copper fee rounding (VERIFY-013). No remote telemetry, auto-trading or new investment discovery.
