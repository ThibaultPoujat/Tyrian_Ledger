# Tyrian Ledger

This repository is the planning and development package for a static,
read-only Guild Wars 2 Trading Post analysis application.

## Purpose

Build a static browser application that:

- receives a versioned public market snapshot generated through the typed GW2 API gateway;
- never requests Guild Wars 2 data or a local API from the browser;
- never automates gameplay, Trading Post actions, or API mutations;
- will provide deterministic beginner fast-flip recommendations for manual in-game action;
- keeps capital and risk preferences browser-local; and
- does not use account API keys, account data, historical market data, crafting, personal history, or an application LLM.

## Development with Codex

Use Codex with GPT-5.6 Terra at High reasoning effort for normal ticket work.
M10 implementation and review work uses Terra with XHigh reasoning effort.
The coding agent is a development tool only; it is not part of the application
runtime.

Start with [AGENTS.md](AGENTS.md), then follow
[the owner/Codex collaboration guide](docs/workflow/codex-collaboration.md).
The remaining Qwen/MTPLX records are historical snapshots, not active
instructions.

## Documentation layout

- `docs/specs/` - normative product/system specifications.
- `docs/architecture/` - technical architecture and API endpoint matrix.
- `docs/security/` - security and French/EU data-protection baseline.
- `docs/testing/` - testing strategy.
- `docs/ux/` - UI/UX rules.
- `docs/adr/` - Architecture Decision Records.
- `docs/context/` - focused context for the current ticket.
- `docs/verification/` - unresolved external-contract and verification register.
- `docs/milestones/` - milestone definitions and ticket contracts.
- `docs/workflow/` - coding-agent execution and delivery rules.
- `AGENTS.md` - Codex entry instructions.

## Agent workflow

1. Give Codex a functional brief for one small outcome.
2. Codex works from one ticket and the minimum relevant context.
3. Use a clean implementation task followed by a fresh review task.
4. Use commits, tests, tickets, and pull requests as the hand-off.
5. The owner confirms functional success and merges after CI passes.

## Normative language

- MUST = mandatory.
- MUST NOT = prohibited.
- SHOULD = strong recommendation; deviation requires rationale.
- MAY = optional.
- ASSUMPTION = provisional until verified.
- VERIFY = must be checked against current authoritative documentation or measured behavior before release.

## Static development handoff

For a clean setup, static build and preview, generator workflow, full test
suite, clean-environment checklist, and known limitations, use the canonical
[static development handoff](docs/development/static-source-release.md).

The handoff is source-only: it is neither a public deployment nor a claim that
all external Guild Wars 2 API contracts have been revalidated.
