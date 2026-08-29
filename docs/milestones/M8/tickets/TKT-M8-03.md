# TKT-M8-03 - Performance and API-request efficiency review

## Milestone
M8

## Goal
Ensure the MVP remains responsive and API-efficient.

## Dependencies
M3-04,M5-04,M7-02

## Acceptance criteria
- [x] Benchmark representative analytical workloads with local fixtures.
- [x] Confirm candidate screening prevents unnecessary deep listings requests.
- [x] Confirm cache and request deduplication behavior.
- [x] Document any known computational limits for crafting search.

## Required tests
- [x] Performance smoke benchmark.
- [x] Request-count regression test.

## Non-goals
- Optimizing prematurely at the cost of correctness.

## Specification references
- `docs/specs/project-spec.md`
- `docs/architecture/architecture.md`
- `docs/testing/testing-strategy.md`
- Relevant milestone context file

## Codex execution
Follow the repository-root `AGENTS.md`; this ticket is the task contract.

## Completion evidence (2026-08-29)

- The live dashboard scan is bounded to the local watchlist and background items. The local
  `MarketSamplingPolicy.MaximumTrackedItemCount` cap of 25 is the workflow bound; no global
  item scan or upstream batch-size assumption was added.
- Aggregate prices are requested once in sorted tracked-ID order. Only positive-quantity,
  positive-price entries where best bid exceeds best ask are sent to the one listings request.
  Empty tracked lists make no market request, and configured scans with no screened candidates
  make no listings request. Missing or partial price/listing data produces no ranked result.
- `MarketFlipScanServiceTests.Fixture_backed_25_item_scan_completes_within_one_second` uses
  only local fixtures, requires a full 25-item scan and ranking to finish in under one second,
  and verifies exactly one prices request and one listings request.
- `CachingGw2ApiClientTests` covers normalized cache keys and concurrent identical price and
  listings cache misses sharing one in-flight transport request. The dashboard integration test
  verifies the live response contract and exact scan request counts.
- Crafting remains explicitly bounded by caller-configured `CraftingSearchLimits.MaximumDepth`
  and `MaximumCandidatePaths`. `CraftingPathAnalyzer` memoizes repeated subproblems, caps recipe
  expansion and combinations, and reports depth/candidate truncation through
  `CraftingSearchDiagnostics` (`WasTruncated` and reason codes).
- VERIFY-004 remains open and unchanged: this ticket makes no claim about an upstream `ids`
  batch limit, paging, or a global market scan.
