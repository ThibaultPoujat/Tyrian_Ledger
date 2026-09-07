# Tyrian Ledger

Tyrian Ledger is a **local-first personal Guild Wars 2 Trading Post assistant**.
It is built for one player who wants trustworthy accounting, market research,
and explicit manual trading decisions without spreadsheets, cloud accounts, or
Trading Post automation.

The project entered a deliberate product pivot beginning with **M12**. The
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

The normal production topology serves the frontend and API from the same
loopback origin. The host validates the `Host` header (for example with an
explicit `AllowedHosts` allowlist), development CORS permits only explicitly
configured trusted origins, and state-changing local endpoints require a
separate cross-origin request/anti-forgery defense; CORS is not treated as CSRF
protection.

## Run the local foundation

TKT-M13-01 introduces `src/Gw2Tp.Web`, the loopback ASP.NET Core host. For
development, install frontend dependencies once and start the host and Vite in
two terminals:

```bash
npm --prefix frontend ci
dotnet run --project src/Gw2Tp.Web/Gw2Tp.Web.csproj
```

```bash
npm --prefix frontend run dev
```

Open `http://localhost:5173`. The React shell calls the host's keyless
`/api/health` endpoint through the development proxy. See
`docs/development/local-runtime.md` for the production publish sequence and the
binding, Host, CORS, and unsafe-request security contract.

## Back up, restore, or clear local data

Tyrian Ledger keeps its database on this computer. The application shows the
exact database and backup-folder locations under **Backup and recovery**. By
default, the database is named `tyrian-ledger.db` in your operating system's
per-user application-data folder, inside `Tyrian Ledger`; backups are in its
`backups` subfolder. A developer can set a different absolute database location
with `TyrianLedger__Database__Path`.

Use **Create local backup** to make a timestamped, consistent copy while the
application is running. To restore one, select the backup file and type
`RESTORE LOCAL DATA`. Tyrian Ledger checks the file and schema before changing
anything, and makes a backup of the current data first. If a backup is invalid
or incompatible, your active data is kept. The local restore control supports
backup files up to 512 MiB; keep a copy in the managed backup folder if a
future database grows beyond that supported upload size.

To remove synced account history and current-order records from the active
database, type `CLEAR PERSONAL DATA`. This keeps shared item metadata and local
settings. Existing backup files are intentionally kept, so delete those files
yourself too if you want to permanently remove every local copy. None of these
operations uploads data or creates cloud backups.

If a restore or backup is interrupted by a crash or power loss, Tyrian Ledger
removes its incomplete hidden staging files and incomplete backup copy at the
next startup or recovery operation. This does not delete retained managed
backups; remove those separately for a complete local privacy purge.

## Financial truth

Authoritative financial behavior is deterministic and tested:

- money is integer copper;
- Trading Post fees use one canonical application policy with separate 5%
  listing and 10% exchange components and a 1-copper positive-sale minimum for
  each; its per-fee round-up behavior remains modeled/provisional while
  VERIFY-013 is open, not independently verified external behavior;
- the listing fee is non-refundable, so cancelling and relisting destroys the
  fee already paid and incurs a new listing fee;
- realized accounting uses explicit transaction history and deterministic lot
  matching;
- unknown cost basis stays unknown rather than becoming zero;
- current and historical market evidence is distinguished from guarantees;
- React renders structured results and does not maintain a competing set of
  authoritative trading formulas.

See `docs/specs/trading-rules.md` for the canonical behavioral rules.

## Project source of truth

For every new coding-agent session, read in this order:

1. `CURRENT.md`
2. `AGENTS.md`
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

## Current implementation state

M12 removed the static Pages product and recorded a clean quality baseline.
TKT-M13-01 adds the local ASP.NET Core runtime foundation without adding an
ArenaNet key, account data, persistence, trading, or recommendation features.
`CURRENT.md` identifies the next permitted ticket.

## Normative language

- `MUST` / `MUST NOT` — mandatory project boundary.
- `SHOULD` — strong default; deviations require a reason.
- `MAY` — optional.
- `VERIFY` — external fact must be checked before it becomes a release fact.
- `BLOCKED` — safe implementation cannot continue without an owner decision or
  missing evidence.
