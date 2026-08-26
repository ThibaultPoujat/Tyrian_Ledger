# TKT-M1-00 - Migrate the development workflow to Codex

## Milestone

M1

## Goal

Replace the retired Qwen/MTPLX development workflow with a lean, durable Codex
workflow while preserving the product, security, and architecture decisions.

## Dependencies

None. This is repository-process work only.

## Acceptance criteria

- [x] A root `AGENTS.md` gives Codex concise, authoritative repository
  instructions.
- [x] Current workflow, context, README, and delivery documentation refer to
  Codex instead of the retired local agent.
- [x] Per-ticket duplicate prompts are removed; tickets and root instructions
  are the only execution contract.
- [x] The owner has a functional-brief template and an explicit implementation
  and independent-review process.
- [x] Historical Qwen/MTPLX evidence is retained but marked non-authoritative
  for active work.
- [x] A real GitHub pull-request template exists and the manifest reflects the
  current layout.

## Required validation

- [x] Search active guidance for retired-agent references and confirm that any
  remaining references are historical records only.
- [x] Validate Markdown links and the documentation map.
- [x] Run the backend test suite and frontend production build.

## Non-goals

- Changing the accepted .NET, React, TypeScript, SQLite, or test-stack ADR.
- Implementing M1-02 or any application feature.
- Rewriting historical M0 evidence.

## Codex execution

Follow the repository-root `AGENTS.md`; this ticket is the task contract.
