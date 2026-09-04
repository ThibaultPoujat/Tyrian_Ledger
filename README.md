# Tyrian Ledger

Tyrian Ledger is a **local-first personal Guild Wars 2 Trading Post assistant**.
It is built for one player who wants trustworthy accounting, market research,
and explicit manual trading decisions without spreadsheets, cloud accounts, or
Trading Post automation.

The project is entering a deliberate product pivot beginning with **M12**. The
repository already contains a strong deterministic C# financial core, a typed
Guild Wars 2 API gateway, order-book simulation, React UI work, and extensive
tests. Those foundations are retained. The M10-M11 public static Pages product
is historical architecture and is being retired in favor of the local-first
runtime described below.

## Product outcome

Tyrian Ledger should answer four practical questions:

1. **How am I actually doing?**
   Reconcile personal Trading Post history into reproducible realized profit,
   open cost basis, current orders, unrealized value, and 7/30/90-day results.
2. **What markets are worth my capital now?**
   Scan current prices and order books using exact fees, depth, liquidity,
   historical spread persistence, stability, and personal turnover evidence.
3. **What should I do next?**
   Produce deterministic, explainable manual actions such as `BUY`,
   `UPDATE BID`, `STOP BIDDING`, `LEAVE SELL LISTING`, `HOLD`,
   `SELL PARTIAL`, and `SKIP`, including prices, quantities, risk limits, and
   reasons.
4. **What should I learn over time?**
   Build an owned historical market dataset, measure which strategies and
   markets work for this player, track medium/long-term positions, and later
   analyze crafting with real opportunity cost.

## Target runtime

```text
React + TypeScript UI
        |
        v
local ASP.NET Core host/API (loopback only by default)
        |
        +--> deterministic Application / Analytics / Domain
        |
        +--> SQLite local database
        |
        +--> typed read-only ArenaNet API gateway
```

The ArenaNet API key is local-only and never enters browser storage, source,
Git, fixtures, prompts, logs, or frontend payloads. The application remains
read-only toward Guild Wars 2: it may recommend a manual action, but it never
places, modifies, or cancels a Trading Post order.

## Financial truth

Authoritative financial behavior is deterministic and tested:

- money is integer copper;
- Trading Post fees are centralized and independently verified;
- realized accounting uses explicit transaction history and deterministic lot
  matching;
- unknown cost basis stays unknown rather than becoming zero;
- current and historical market evidence is distinguished from guarantees;
- React renders structured results and does not maintain a competing set of
  authoritative trading formulas.

See `docs/specs/trading-rules.md` for the canonical behavioral rules.

## Project source of truth

For every new coding-agent session, read in this order:

1. `AGENTS.md`
2. `CURRENT.md`
3. `docs/context/permanent-context.md`
4. the current milestone context under `docs/context/`
5. the assigned ticket under `docs/milestones/<M>/tickets/`
6. `docs/verification/VERIFY-REGISTER.md`
7. only the specialized specifications, ADRs, and source files needed for that
   ticket

The active product specification is `docs/specs/project-spec.md`. The target
technical design is `docs/architecture/architecture.md`. The data model is
`docs/architecture/data-model.md`. Historical ADRs remain readable so agents
can understand why obsolete code exists, but an ADR marked **Superseded** is not
active guidance.

## Milestones

M0-M11 are project history. The personal trading-assistant pivot continues the
sequence at **M12** and is planned through **M22**. See
`docs/milestones/INDEX.md` for the authoritative dependency order.

The key product checkpoint is M19: by its end, the application should combine
personal state, live market evidence, owned history, and risk limits into the
primary **What Should I Do?** screen.

## Codex workflow

One implementation ticket is one bounded implementation session. The agent
works on a dedicated branch/worktree, validates the ticket, opens a pull
request, writes a short **functional summary** of what changed for the user,
and stops. A fresh independent review session then evaluates the PR.

Use `.codex/skills/tyrian-pr-review/SKILL.md` for the standard review procedure.
The owner merges only after acceptance criteria, tests, review findings, and
functional behavior are satisfactory.

## Current transition state

The source tree still contains the M10-M11 static Pages runtime at the beginning
of M12. Do not mistake presence for architectural authority. `CURRENT.md` states
which transition ticket is active and what may safely be removed next.

## Normative language

- `MUST` / `MUST NOT` — mandatory project boundary.
- `SHOULD` — strong default; deviations require a reason.
- `MAY` — optional.
- `VERIFY` — external fact must be checked before it becomes a release fact.
- `BLOCKED` — safe implementation cannot continue without an owner decision or
  missing evidence.
