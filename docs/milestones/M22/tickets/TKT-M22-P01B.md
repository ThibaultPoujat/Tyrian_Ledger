# TKT-M22-P01B — Durable Step-Bound Completion Commands

GitHub issue: #154

Milestone: M22. Risk: **R3**, execution/persistence authority. **SOL-GATED**.
Implementation: **Luna High explicitly permitted** for this bounded contract.
Fresh independent **Sol XHigh** review after required validation/CI is green.
Ready after #149 / PR #152 and checkpoint preparation #153 are merged into develop.

## User outcome and observed gap

Reporting a completed step twice, including after a lost response or restart,
has one durable effect and never reports the following step by accident.

At baseline `d6e960a59901e0297513bdea4cfd06152f758c24`, `PlanStepCompletion`
contains quantity, unit price and NotPerformed only. `CompleteAsync` reloads the
current plan and reports its current step. `SaveAsync` checks the loaded revision,
which protects competing writes but not a sequential retry after the first save.
The next request can load the advanced plan and report another step. A disabled
button alone cannot protect this server boundary.

## Entry points

- `src/Gw2Tp.Application/Plans/PlanContracts.cs`: explicit application command/result/receipt contracts and plan/event identity.
- `src/Gw2Tp.Application/Plans/PlanOrchestrationService.cs`: report/cancel transitions; preserve existing effects and resource aggregation.
- `src/Gw2Tp.Infrastructure/Persistence/SqlitePlanRepository.cs`: scoped plan reads, optimistic concurrency and atomic durable mutation.
- `src/Gw2Tp.Infrastructure/Persistence/SqliteSchemaMigrator.cs` only if the chosen receipt storage requires a focused schema change.
- `src/Gw2Tp.Web/Hosting/PlanEndpoints.cs`: completion transport mapping and committed revision/step context in responses.
- `frontend/src/PlanPanel.tsx` and `PlanPanel.test.tsx`: transport metadata and logical retry identity only.
- Existing application, SQLite integration and plan endpoint test suites; `tests/Gw2Tp.Web.E2E/tests/transition-shell.spec.ts` only where fixtures/contracts require adjustment.

## Command contract

Define typed application semantics; endpoints map them rather than own a second
implementation of the transition. Existing quantity/price/NotPerformed payloads
remain supported for this incremental safety fix, with their current validation.
Their future approved read-only/automatic-observation UX is a separate ticket.

Each request identifies:

- the execution/plan ID from the route;
- the exact displayed step ID;
- the expected durable execution revision (not the proposal's Version);
- a nonempty logical command ID, stable for retries of that user operation;
- the operation kind (`report performed` or `not performed`) and its payload.

The current account is resolved by the trusted local host, never accepted from
an untrusted request as authorization. Bind the operation to that account and
execution. Missing/invalid identity metadata fails explicitly; do not fall back
to whichever step happens to be current. Wire revision values losslessly.

### New logical command

1. Load the scoped durable execution. Verify expected revision, target step and
   allowed state, then validate the command payload using existing transition
   policy. Stale/wrong-step commands are conflicts, not automatically rebased.
2. Atomically commit the state transition AND a durable receipt identifying the
   command, canonical payload identity, target step, resulting revision and
   relevant event/result identity. No receipt without its transition, no
   transition without its receipt. Invalid input changes neither.
3. Preserve optimistic concurrency, account isolation and checked resource
   semantics. The persistence boundary must enforce admission; validation only
   in React or before the final write is insufficient.
4. Return the committed revision, not the pre-save object revision. Check read,
   start, complete and undo response construction so the client gets usable
   current concurrency metadata; changing undo's business semantics is out of scope.

### Replay and conflicts

Look up a matching durable receipt before rejecting the original revision or
step as stale. Compare the submitted canonical payload with the stored receipt;
do not resolve defaults from a different current step when fingerprinting a retry.

- Same scoped command ID and same operation/payload: acknowledge the original
  command as already applied, with the original receipt identity and no new
  event, effect or revision increment. It must work after step advance and process
  restart. Do not replay the transition merely because the current state differs.
- Same scoped command ID with a different step, revision, operation or payload:
  explicit conflict, no mutation. Never silently reinterpret the old command.
- Different command IDs competing for one step/revision: at most one commits;
  losers receive a stale/conflict result and must refresh before a new user action.
- Concurrent copies of the same command: one commit, then equivalent successful
  acknowledgement for the other copies, not duplicate effects or an unhandled
  database error. A unique constraint/CAS alone is not the complete replay path.
- After local undo or later reconciliation, replay acknowledges history without
  reinstating the old effect. A fresh action requires a new command ID and current
  step/revision. Receipts survive these transitions while the execution is retained.
- Terminal/cancelled plans still support scoped receipt lookup. Do not make
  replay depend only on `GetStartedAsync`, which excludes some terminal states.

Return acknowledgement separately from current execution state: the client must
not render a saved, outdated response snapshot as the current next instruction.
Refreshing current state after acknowledgement is acceptable. Replays must not
rerun candidate discovery/reconciliation just to decide whether they committed.
Resolve current account scope safely; this does not require redesigning the
whole local decision/read-model pipeline.

## Client transport integration

Send the explicit step/revision context shown to the user. Retain one logical
command ID and its original payload through a pending request or unknown network
outcome; an explicit retry of that same operation reuses them. A subsequent user
action on a different displayed step creates a new ID. Do not automatically turn
a failed or timed-out request into an instruction for the newly current step.

Keep a lost-response retry associated with its original step even if a query
refresh advances the displayed execution. Store pending operations above the
individual PlanCard so an advanced or terminal plan cannot discard their identity.

The bounded recovery interaction is the existing **Actualiser** action: if a
command has an unknown network outcome, this explicit refresh first retries that
original pending command once with its original ID/context/payload, then reads
current plans. With no pending command, Actualiser remains a normal read. Do not
schedule an automatic retry loop or retry on background query refresh. Concurrent
refresh clicks coalesce while that retry is in flight; if it fails ambiguously
again, retain the pending operation for the next explicit Actualiser action.
Keep it even when its original card disappears after cancellation/terminal state.
After a definitive acknowledgement or rejection, clear that pending operation and
read current state. A new completion action on step B uses B's identity and a new
command ID; it must never act as the retry trigger for pending step A. Never send
a pending operation under a newly selected account's context.

Network/request tests must prove this recovery path and the distinction between
retrying A and intentionally acting on B. The server remains safe if another tab
or direct caller uses another ID. No new quantity/price UI, layout, copy, style or
visual redesign is part of this ticket. Keep changes to transport/state wiring.
No screenshot-based claim of P05 completion is permitted. If rendering changes
become necessary, record the scope change and apply the normal visual evidence
contract; do not silently expand into the approved screen rebuild.

## Required acceptance vectors

| Case | Required result |
|---|---|
| Report step A, advance to B, retry identical command A | One A event/receipt; B unreported; no additional cash/inventory effect |
| Two simultaneous identical commands | One committed transition; equivalent acknowledgement/receipt |
| Two different commands for the same step/revision | At most one commits; loser conflicts |
| Same ID, changed quantity, price, NotPerformed, step or revision | Conflict; original receipt and plan unchanged |
| Wrong step, stale revision, missing command ID or invalid payload | Explicit failure; zero persisted effects/receipts |
| Commit succeeds but response is lost; reopen repository and retry | Original receipt returned, no new transition |
| Failure before transaction commit | Neither receipt nor mutation persists; later valid retry can commit once |
| NotPerformed cancels/invalidates a step; retry after terminal state | Original acknowledgement; no second cancellation or next-step mutation |
| Report, then undo, then retry original report | Acknowledges original history; does not reapply undone effects |
| Report later reconciled/confirmed; retry | Remains once-only; no new provisional effect |
| Other account tries the execution/receipt | No mutation and no private receipt/plan disclosure |
| Read/start/complete/undo result used for next fresh command | Actual committed revision; no systematic stale-revision rejection |
| Existing client double click / response loss / refresh while pending | Same logical operation retains step, revision, payload and ID; never silently targets next step |
| Explicit Actualiser after A advances to B or A's card disappears | One retry of original A, then read; no report for B and no recreated terminal card |
| Automatic query refresh or explicit new action on B while A is pending | Background read does not replay; B action has distinct B identity and is never interpreted as retry A |

Use public endpoint/application and real SQLite integration tests, not only a
helper test. Include at least one reproducible pre-fix regression. Keep #149's
aggregation and residual acquire→craft→sell regressions green.

## Validation

```bash
dotnet test tests/Gw2Tp.Application.Tests/Gw2Tp.Application.Tests.csproj --filter FullyQualifiedName~Plan
dotnet test tests/Gw2Tp.Infrastructure.Tests/Gw2Tp.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqlitePersistenceIntegrationTests
dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj --filter FullyQualifiedName~Plan
npm --prefix frontend test -- --run src/PlanPanel.test.tsx
npm --prefix frontend run build
node --test .github/scripts/*.test.mjs
```

Run any new command-service test class if not matched by those filters, then
required CI including the existing browser matrix. Record environment startup
failures honestly; remote green CI can supply missing local .NET evidence.

## Boundaries and follow-up

This delivers once-only local commands, not API confirmation. Reconciliation
freshness/partial evidence, partial execution replanning, passive continuation,
settled/repeatable executions, inventory ingestion/protections, global account/
restore epochs and full command latency remain later P01–P03 tickets. #150 stays
open. Preserve read-only game access, secret boundaries and current local-host
security. No new external API assumptions or fee/retention policy changes.

If receipt storage needs a schema change, follow the pre-0.1 policy and preserve
backup/restore/integrity capabilities. Do not introduce receipt expiry or broad
destructive cleanup. The full reset/restore epoch guarantee is not claimed here.

Open Draft PR targeting develop with `Closes #154`, actual M22 milestone and
commit prefix `[TKT-M22-P01B]`. Run `$tyrian-pr-review` with fresh Sol XHigh after
green validation/CI. Owner merges. Stop after this ticket; no next-ticket coding.
