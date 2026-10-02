# P03C1 — Local Command Evidence

Runtime/test head: `ac73f75d898c03998e34e440e16ababda3886942`. Delivery documentation/images follow separately;
final-head CI and fresh independent Sol XHigh review belong in the PR and #180's B3 row.

Complete and Undo admit only an already-bound host account/credential session,
generation and store incarnation. Native credential observation precedes matching
the opaque view token, profile and receipt access. Missing bound scope rejects
locally; a separate existing refresh/context read verifies and binds identity.
No database profile or browser identity can establish that authority.

The command services contain no account, market, crafting or recommendation
reads. SQLite preserves account ownership, revision CAS, event/state/receipt
atomicity and selected-location/actor/protection/age/global resource revalidation.
Undo returns the transaction's revalidated state and preserves events/receipts.
Its body now requires the displayed `expectedRevision`; both commands require
`X-Tyrian-Ledger-Account-View-Scope`. Frontend transport supplies these values.

Applied and receipt replay invalidate using the captured generation with a
non-request cancellation token. Undo revision conflict also clears a potentially
missed current projection without another reversal. Delayed old-generation
publication/invalidation still rejects. Recovery transitions remove the bound
account and require fresh verification. TP read groups remain serialized by a
separate semaphore; local/recovery operation leases cover only persistence.

## Acceptance evidence

| Vector | PASS evidence |
|---|---|
| Indefinitely held unrelated account/market reads | `Actual_host_complete_and_undo_commit_before_account_and_market_HTTP_barriers_release`: real host/SQLite commits before both barriers release, zero new gateway reads/HTTP attempts. A second TP read group remains queued; local operation gate is free. |
| Unbound startup/transition/clear/restore | Actual public-route startup test and `Unbound_scope_rejects_known_receipts_and_undo_before_lookup_without_HTTP`: structured local rejection, no acknowledgement/receipt disclosure, zero reads. |
| A→B→A / ownership | `AccountWorkBoundaryTests` opaque token rejection precedes null profile/receipt seams; retained real `AccountWorkGenerationIntegrationTests` credential/account/SQLite fences and restart receipt checks. |
| Double click, revision concurrency, replay | Actual-host two-command race has one effect/conflict; identical retry returns the receipt. SQLite concurrent undo has one reversal/conflict; receipt survives undo. |
| Moved/stale/protected/global resources/actor | New independent-source SQLite vectors plus retained holdings persistence, projection/protection/crafting-actor tests. Rejection writes no receipt/effect; retained protection floor cannot be erased by a later capture. |
| Browser cancels immediately after durable effect | Actual production command/fence/SQLite tests for both commands: revision/event persists and invalidation completes. |
| Clear/restore queued command | Real host recovery endpoints reject captured delayed command; no event/revision in restored backup and no profile after clear. |
| Post-commit failure | Injected invalidation interruption recovers by identical receipt or old undo revision; no second event/revision. |
| Undo descendants / partial / NotPerformed | Retained Application partial/confirmed-descendant pause and orchestration vectors; new local undo uses that same policy. Partial quantities and canonical financial policy are unchanged. |

## Validation and measurements

- `dotnet test TyrianLedger.slnx -c Release --no-build --verbosity quiet`: **882 passed** (4 Domain, 21 Analytics, 396 Application, 360 Infrastructure, 101 Web). Later test-only serialization assertions: **17 passed** (6 Application + 11 Web).
- `npm --prefix frontend test`: **71 passed**; `npm --prefix frontend run build`: passed and production preview isolation verified.
- `node --test .github/scripts/*.test.mjs`: **27 passed**.
- Chromium `p03c1-local-command.spec.ts`: **1 passed**; real host frontend at 1920×1080, keyboard undo, axe, 100%/125% no horizontal clipping. [Images and reference comparison](../../../ux/evidence/TKT-M22-P03C1/README.md).
- Whitespace and staged diff inspection passed. Final-head CI/review are recorded in the PR.

[Safe raw samples](latency.json) use actual TestServer routing/production command
services and real SQLite, a synthetic typed account gateway barrier and an actual
public HTTP handler barrier. CPU Apple M1 Pro, macOS 26.6.2, ARM64, .NET 10.0.5,
10 logical processors, Release, temporary local filesystem, warm initialized
account/store. Twelve completion/undo pairs, each before either barrier release:
completion median **1.628 ms**, p95 **27.592 ms**;
undo median **1.618 ms**, p95 **7.566 ms**.
The response header `X-Tyrian-Plan-Command-Timing` separates local admission/profile,
commit and invalidation milliseconds; it contains no identities or money.
All measured commands meet 500 ms here. Barrier ordering/zero upstream work are
the correctness assertions. These samples establish neither Windows performance
nor authenticated ArenaNet coherence/latency. Native credential-store access remains
local and required; this fixture runs with an empty real key and synthetic account evidence.

Reproduce the safe latency trace (no private account or key):

```sh
TYRIAN_LEDGER_LOCAL_COMMAND_EVIDENCE=/private/tmp/p03c1-latency.json dotnet test tests/Gw2Tp.Web.Tests/Gw2Tp.Web.Tests.csproj -c Release --filter FullyQualifiedName~Actual_host_complete_and_undo --verbosity quiet
```

VERIFY-008/013/016/017 remain OPEN. No new upstream fact is resolved. No start,
settlement/repetition, positive physical confirmation, generalized residual
replanning, passive continuation, persisted decisions, fee or permission policy
change. P03C2 and live P05C shell convergence retain their later scope.
