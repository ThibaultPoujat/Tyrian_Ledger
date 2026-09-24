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

Canonical domain lifecycle:

`Opportunity -> Plan -> Steps -> Reconciliation -> Outcome`

Market/account intelligence feeds opportunity discovery but is not a lifecycle
stage. A Signal is also not a lifecycle stage: it is a presentation/eligibility
concept for a sufficiently safe, profitable, relevant and compatible
opportunity/plan action that should be surfaced to the owner now. No-action
states such as `WAIT`, `HOLD`, `KEEP BID`, harmless outbid/undercut, `SKIP` and
`REVIEW` normally remain silent on the main action feed.

See `docs/specs/signals.md` for the complete product model and `docs/ux/ux.md`
for the active interface contract.

## Durable delivery rules

- One implementation ticket normally equals one implementation session.
- Authority is split by concern:
  - GitHub merged PRs, issue open/closed state, and milestone assignment/title are authoritative for **operational delivery state**;
  - issue #98 and `docs/milestones/INDEX.md` are authoritative for **execution order and the next valid ticket**;
  - `docs/workflow/model-effort-guide.md` is authoritative for **review effort and the active explicit Sol-gate list**.
- Issue #98 and `docs/milestones/INDEX.md` must agree on execution order. If they conflict, repair the source-of-truth contradiction instead of silently choosing one or inferring numeric issue order.
- `CURRENT.md` contains durable narrative plus the generated live-state block below. The generated block is a derived handoff/cache view, not an independent authority. The owner should not manually maintain normal merge handoff state.
- TKT-M21-S01 / #129 provides a deterministic post-merge workflow/script that combines those authorities and updates only the generated block without AI/model quota.
- A coding agent still compares each generated field with its owning authority at session start and repairs stale generated state if the workflow failed or has not yet run.
- Historical ADRs/tickets remain useful evidence but are not active instructions when superseded by current source-of-truth docs/tickets.

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

The preferred owner sequence from here is:

1. #129 / TKT-M21-S01 — self-healing `CURRENT.md` generated live state using the explicit authority split;
2. #130 / TKT-M21-S02 — prototype/validate the Signals second-screen UX displayed as `Mes Signaux`;
3. #131 / TKT-M21-S03 — usable Signals MVP + primary navigation;
4. #132 / TKT-M21-S04 — remove/archive superseded UI/docs/code after MVP;
5. #133 / TKT-M21-S05 — plan orchestration, Active/Passive paths, resource reservations, reversible shadow state, Undo and reconciliation;
6. #93 / TKT-M21-02 — crafting economic truth and direct procurement alternatives;
7. #94 / TKT-M21-03 — bounded crafting opportunity paths + guided crafting UI displayed as `Artisanat`;
8. #95 / TKT-M22-01 — continuous decision loop + actionable notifications;
9. #96 / TKT-M22-02 — security/recovery/E2E/accessibility/local packaging;
10. #97 / TKT-M22-03 — Signal-plan outcome evaluation and strategy attribution.

#129 is the preferred maintenance predecessor, but it must not delay the MVP path.
While #129 remains open, #130 may start after #128 merges if #129 becomes
non-trivial or would delay #131. Once #130 closes, #131 may continue on the
same allowed route because #131 depends on #130, not on completion of #129.
In that fallback, agents use the documented session-start reconciliation/manual
generated-block repair.

Do not use numeric issue ordering to skip #129-#133 and begin #93 early.

## Generated live state

The block between the markers is machine-owned once #129 lands. Durable prose
outside the markers must never be rewritten by the generated-state updater.

<!-- BEGIN GENERATED LIVE STATE -->
- Last completed implementation ticket: `TKT-M21-03 / #94`
- Last merged implementation PR: `#143`
- Active milestone: `M22 — Convenience, Hardening, and Evaluation`
- Preferred next implementation ticket: `TKT-M22-01 / #95`
- Allowed non-blocking alternate: `None`
- Explicit active Sol gates: `TKT-M21-S05 / #133`, `TKT-M21-02 / #93`, `TKT-M21-03 / #94`, `TKT-M22-02 / #96`
- Authorities: operational state = `GitHub`; execution order = `issue #98 + docs/milestones/INDEX.md`; review gates = `docs/workflow/model-effort-guide.md`
<!-- END GENERATED LIVE STATE -->
