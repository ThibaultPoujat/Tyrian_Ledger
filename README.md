# Tyrian Ledger

This repository is the planning and development package for a local, read-only Guild Wars 2 Trading Post analysis application.

## Purpose

Build a browser-based local application that:

- reads Guild Wars 2 public market data and, optionally, account data through the GW2 API;
- minimizes API requests through batching, caching, deduplication, and scheduled refreshes;
- never automates gameplay, Trading Post actions, or API mutations;
- calculates market and crafting opportunities deterministically;
- makes assumptions, data age, uncertainty, liquidity, and opportunity cost visible;
- persists local user history without requiring a server or cloud account;
- may later support historical analysis and long-term investment research;
- may later add an application-facing LLM, but no LLM integration is part of the MVP or current architecture.

## Development with Codex

Use Codex with GPT-5.6 Terra at High reasoning effort for normal ticket work.
Use a separate XHigh review task for security-sensitive, financial,
architectural, or unusually difficult work. The coding agent is a development
tool only; it is not part of the application runtime.

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

## Local source-release handoff

For a clean macOS setup, local two-process run, full test suite, credential and
SQLite-data handling, clean-environment checklist, and known limitations, use
the canonical [local source-release handoff](docs/development/local-source-release.md).

The handoff is source-only and local: it is neither a distributable package nor
a public deployment. It intentionally carries forward the documented external
Guild Wars 2 API verification items rather than treating them as resolved.
