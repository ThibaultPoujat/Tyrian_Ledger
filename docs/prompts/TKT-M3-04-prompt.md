You are the implementation agent for the Tyrian Ledger project.

Read first:
- docs/context/permanent-context.md
- docs/context/milestone-context-M3.md
- docs/verification/VERIFY-REGISTER.md
- docs/tickets/TKT-M3-04.md

Then read the opportunity-scoring specification and existing analytics models relevant to this ticket.

## Mission

Complete TKT-M3-04 only.

Rank opportunities using transparent, deterministic weights and penalties.

Acceptance-critical work:
- produce deterministic scores for identical inputs/configuration;
- configure weights without code changes where specified;
- include profit, capital efficiency, liquidity, freshness, risk, and complexity where applicable;
- produce UI-facing explanation metadata.

## Non-goals

- LLM-generated ranking;
- opaque machine-learning scores;
- changing the underlying opportunity calculations.

## Hard rules

- Score only from explicit inputs.
- Keep scoring explainable and reproducible.
- Do not invent risk or liquidity semantics; use project policy or VERIFY.
- Add unit tests for deterministic scoring, weights, penalties, and edge cases.

## Execution

1. Inspect ticket and existing analytics models.
2. Make a maximum five-step plan.
3. Implement the smallest scoring service/configuration.
4. Add focused unit tests.
5. Run tests and inspect the diff.
6. Stop.

Do not repeatedly reread unchanged files. After two failed attempts, report the blocker.

## Validation

Confirm identical inputs/configuration produce identical scores and explanation metadata.

## Delivery

Follow `docs/workflow/delivery-protocol.md`.
Do not merge the pull request.

## Final report

Return only files changed, acceptance-criteria status, validation/results, VERIFY items,
known limitations/blockers, and the verified PR URL when complete.
