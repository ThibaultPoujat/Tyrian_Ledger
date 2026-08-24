You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M8.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M8-03.md

Then read performance/API-efficiency and crafting-search requirements relevant to this ticket.

## Mission

Complete TKT-M8-03 only.

Verify that the MVP remains responsive and API-efficient.

Acceptance-critical work:
- benchmark representative analytical workloads with local fixtures;
- confirm candidate screening avoids unnecessary deep listings requests;
- confirm cache and request deduplication behavior;
- document known computational limits for crafting search.

## Non-goals

- premature optimization that changes correctness;
- live API load testing;
- rewriting algorithms without evidence.

## Hard rules

- Benchmark locally with deterministic fixtures.
- Do not infer live API performance from synthetic benchmarks.
- Preserve correctness and API minimization.
- Add performance regression checks only when they are stable and justified.

## Execution

1. Inspect ticket, performance requirements, and relevant code paths.
2. Make a maximum five-step plan.
3. Benchmark the defined workloads.
4. Apply only evidence-backed optimizations in ticket scope.
5. Validate and inspect the diff.
6. Stop.

Do not repeatedly rerun the same benchmark without changing the relevant condition. After two
failed attempts, report the blocker.

## Validation

Record workload, environment, measurement method, and result. Confirm cache/dedup behavior and
document computational limits rather than promising universal performance.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
