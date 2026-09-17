# Current Project State

Last durable-context update: 2026-09-17

## Durable product direction

Tyrian Ledger is a **local-first second-screen Guild Wars 2 profit assistant**.
It continuously turns account/market evidence into the smallest useful set of
concrete manual actions for the owner to perform in Guild Wars 2.

Target runtime remains:

`React UI -> loopback ASP.NET Core host/API -> deterministic Application/Analytics -> SQLite + typed read-only ArenaNet gateway`

The application never automates gameplay or Trading Post mutations. Authoritative
financial/accounting/recommendation behavior remains deterministic, testable and
integer-copper based.

The primary daily Signals surface is displayed as **`Mes Signaux`**. Target
displayed primary navigation is:

- `Mes Signaux`;
- `Artisanat`;
- `Réglages`.

Repository-facing documentation, filenames, code, identifiers and internal model
terminology are English. French is the product's displayed language: all
user-facing UI/UX labels, actions, messages, errors, explanations, empty states
and accessibility text are written in French.

The 0.1 product focus is Trading Post flipping/trading plus crafting. Existing
investment-position/staged-exit infrastructure is preserved, but investment
opportunity discovery/seasonality is deferred.

Canonical internal product flow:

`Intelligence -> Opportunity -> Plan -> Steps -> Signal -> Reconciliation -> Outcome`

A Signal is an opportunity sufficiently safe, profitable, relevant and compatible
with the owner's current state to justify a concrete manual action. No-action
states such as `WAIT`, `HOLD`, `KEEP BID`, harmless outbid/undercut, `SKIP` and
`REVIEW` normally remain silent on the main action feed.

See `docs/specs/signals.md` for the complete product model and `docs/ux/ux.md`
for the active interface contract.

## Durable delivery rules

- One implementation ticket normally equals one implementation session.
- GitHub merged PR/closed issue/milestone state is authoritative for **live
  delivery state**.
- `CURRENT.md` contains durable narrative plus the generated live-state block
  below. The owner should not manually maintain normal merge handoff state.
- TKT-M21-S01 / #129 will add a deterministic post-merge workflow/script that
  updates only the generated block without AI/model quota.
- Until #129 lands, a coding agent must compare the generated block with GitHub
  at session start and repair any stale live state before relying on it.
- Historical ADRs/tickets remain useful evidence but are not active instructions
  when superseded by current source-of-truth docs/tickets.
- Review effort and the explicit Sol gate are defined centrally in
  `docs/workflow/model-effort-guide.md`.

## Known foundation

The local-first pivot foundation is complete through TKT-M21-01 / #92, merged in
PR #127 on 2026-09-16. This includes local runtime/security, durable SQLite data,
trustworthy FIFO accounting, dashboard/current orders, live scanner/depth,
owned market history, deterministic history/score/sizing/recommendation engines,
personal turnover/performance evidence, investment-position tracking, and
normalized crafting account/capability ingestion.

PR #127's final recorded regression baseline was 481 .NET tests plus all five
GitHub checks green after its review fix. VERIFY-004, VERIFY-005, VERIFY-008 and
VERIFY-013 remain relevant open external-contract uncertainties; consult the
VERIFY register rather than assuming external behavior.

## Active execution order

The owner-approved sequence from here is:

1. #129 / TKT-M21-S01 — self-healing GitHub-authoritative `CURRENT.md` live state;
2. #130 / TKT-M21-S02 — prototype/validate the Signals second-screen UX displayed as `Mes Signaux`;
3. #131 / TKT-M21-S03 — usable Signals MVP + primary navigation;
4. #132 / TKT-M21-S04 — remove/archive superseded UI/docs/code after MVP;
5. #133 / TKT-M21-S05 — plan orchestration, Active/Passive paths, resource reservations, reversible shadow state, Undo and reconciliation;
6. #93 / TKT-M21-02 — crafting economic truth and direct procurement alternatives;
7. #94 / TKT-M21-03 — bounded crafting opportunity paths + guided crafting UI displayed as `Artisanat`;
8. #95 / TKT-M22-01 — continuous decision loop + actionable notifications;
9. #96 / TKT-M22-02 — security/recovery/E2E/accessibility/local packaging;
10. #97 / TKT-M22-03 — Signal-plan outcome evaluation and strategy attribution.

Do not use numeric issue ordering to skip #129-#133 and begin #93 early.

## Generated live state

The block between the markers is machine-owned once #129 lands. Durable prose
outside the markers must never be rewritten by the generated-state updater.

<!-- BEGIN GENERATED LIVE STATE -->
- Last completed implementation ticket: `TKT-M21-01 / #92`
- Last merged implementation PR: `#127`
- Active milestone: `M21 — Signals and Crafting Intelligence`
- Next valid implementation ticket: `TKT-M21-S01 / #129`
- Explicit active Sol gates: `#133`, `#93`, `#94`, `#96`
- Live-state authority: `GitHub`; reconcile this block before use if it disagrees
<!-- END GENERATED LIVE STATE -->