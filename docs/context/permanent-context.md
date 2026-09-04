# Permanent Context

## Identity

Tyrian Ledger is a local-first personal Guild Wars 2 Trading Post assistant.
Codex/coding agents are development tools only; no application LLM participates
in runtime financial truth.

## Hard constraints

- Read-only toward Guild Wars 2; no gameplay or Trading Post automation.
- A dedicated ArenaNet API key may be used only by the local host/infrastructure
  for verified read-only endpoints.
- No secret value in source, browser code/storage, frontend payloads, logs,
  fixtures, prompts, tests, commits, PRs, or SQLite.
- Browser never accesses the OS secret store or ArenaNet directly.
- All ArenaNet access goes through typed gateway abstractions with request
  policy/rate limiting.
- External DTOs stay separate from domain/application models.
- Authoritative money is integer copper.
- Financial/accounting/recommendation logic is deterministic and tested.
- Unknown cost basis/insufficient history remains explicit.
- Never invent ArenaNet fields, permissions, quotas, endpoints, timestamp
  semantics, fee behavior, or limits; use VERIFY.
- No runtime LLM or opaque ML financial model.

## Target stack

- .NET 10 / ASP.NET Core loopback local host
- React + TypeScript frontend
- SQLite local persistence
- existing Domain/Application/Analytics/Infrastructure libraries
- xUnit and frontend tests
- Playwright E2E

## Target architecture

`React -> local ASP.NET Core API -> Application/Analytics -> SQLite + typed ArenaNet gateway`

The M10-M11 public static Pages architecture is historical and superseded for
the personal-assistant direction. Transition code may remain only until its M12
retirement ticket.

## Product priorities

1. trustworthy personal TP accounting;
2. fee-aware live scanner with real depth/liquidity;
3. owned historical market observations;
4. explainable `What Should I Do?` actions and risk sizing;
5. personal turnover/fill learning and investment tracking;
6. crafting economics;
7. alerts, packaging, and recommendation evaluation.

## Required session context

1. `CURRENT.md`;
2. `AGENTS.md`;
3. this file;
4. current milestone context;
5. assigned ticket;
6. relevant `docs/verification/VERIFY-REGISTER.md` entries;
7. only specialized source/spec/ADR files needed for the ticket.

Do not load all historical milestone/ticket documents.

## Session rule

One implementation ticket normally uses one implementation session. Work in one
isolated branch/worktree, plan briefly, implement, validate, inspect the diff,
open a PR, provide a functional summary, and stop.

Every PR receives a fresh independent review using
`.codex/skills/tyrian-pr-review/SKILL.md`. Git/tests/tickets/docs are the durable
handoff; previous chat context is not.

## VERIFY versus BLOCKED

`VERIFY` means an external fact is unresolved but safe implementation can
continue without assuming it. `BLOCKED` means implementation would be unsafe or
impossible without evidence or an owner decision.

Record material uncertainty once with enough context; do not repeatedly
research the same unresolved fact without new evidence.

## Owner decision gates

The owner controls product scope, architecture/ADR changes, API permission/key
requirements, canonical financial policy, destructive persistence/retention,
network exposure, paid/production dependencies, release/merge, and any proposal
to cross the read-only automation boundary.
