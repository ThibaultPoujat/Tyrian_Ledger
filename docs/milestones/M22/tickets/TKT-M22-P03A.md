# TKT-M22-P03A — Account and Store Generation Fences

GitHub issue: #170

Milestone M22. Risk R3. Implementation: GPT-6.1 Sol High.
Review SOL-GATED: fresh independent GPT-6.1 Sol XHigh after green CI.
Ready after #169 and #168 merge with their typed handoffs and B2 evidence.

## Outcome and inspected gap

Work admitted for an earlier account/store cannot publish, commit, or retrieve
private receipts after account changes, clear or restore. Returning A→B→A never
revives A's old browser scope. This fence precedes persistent P02 ingestion.

AccountViewScopeTokenService currently stores one process-local token per
account ID. Clearing/restoring does not rotate that token. TP synchronization
uses PersonalDataOperationGate, while crafting snapshot replacement and cached
plan decisions have different lifetimes. Keep P01B's scope-before-receipt rule.

## Entry points

- Web/Hosting/AccountViewScopeTokenService.cs, PlanEndpoints.cs, LocalDataEndpoints.cs
- Existing loop/decision/status cache invalidation and hosting DI composition
- Application/LocalData operation gate, PersonalTradingPost synchronization service,
  Crafting snapshot service; P02A/B collector boundaries
- Infrastructure/Persistence/SqliteLocalDataRecoveryService.cs, scoped account
  snapshot/TP/plan repositories, schema validation; Secrets/Gw2ApiKeySources.cs
- PlanCompletionRetry/endpoint tests, synchronization tests, real SQLite recovery
  and persistence integration tests

## Contract

1. Define one application account-work context: trusted AccountScope plus opaque
   credential-session identity and store incarnation/generation. Capture it
   before asynchronous work. Keep credentials in Infrastructure memory only;
   neither key nor key hash/fingerprint is stored/exposed. Explicitly distinguish
   browser view scope from trusted internal account ID.
2. Maintain a durable nonsecret store incarnation, with a fresh nonreused session
   generation at host startup and every observed account/credential transition,
   personal clear, successful restore and rollback/recovery that invalidates
   captured work. A restored database must never restore an admissible old
   browser token. Rotate before exposing restored state. Use fresh opaque IDs,
   not a restored counter alone. Document the small state-transition diagram and
   failure behavior; follow the existing pre-0.1 clean-schema policy.
3. Account/key transition admission is host-owned. No new key-write API or
   permission is introduced. When the existing credential source changes, bind
   every authenticated read bundle to one captured credential and its account;
   reject mixed-account batches. Recheck current credential-session/context
   before commit through an internal safe adapter. External OS-store changes
   are observed on reads; do not claim instantaneous unobservable detection.
4. Register the shared fence in production DI. Route existing private writes
   (TP success/failure status, crafting snapshots, plan starts/completions/
   reconciliation/receipts) and loop/read-model publication through it. Reject
   stale context inside the same short critical section/transaction as mutation.
   A cancellation signal or check before HTTP is insufficient. Include a table
   of actual producers/writers/publications and their guard; no “all done” claim
   from guarding only the new collector. #171 uses this same seam.
5. Admission/capture, asynchronous fetch, commit-or-reject are separate phases.
   Never hold the new generation gate during HTTP or acquire locks in opposite
   order. Existing operation/database gates remain where necessary; establish
   and test one lock order for generation transition, recovery and write commit.
   This ticket adds correctness fences, not the general scheduler/cache or
   local-command-latency redesign from P03B/C.
6. Clear/restore enters a quiescing state, rejects new old-context work, validates
   and changes storage using existing staged recovery guarantees, then publishes
   a fresh usable generation only on success. Failed restore preserves valid
   live data and backup safety; invalidating in-flight contexts is allowed, but
   no half-restored ready state. Persistence/generation failure keeps private
   operations unavailable rather than falling back to an unguarded/noop gate.
7. Guard commands with current browser scope and generation BEFORE receipt
   lookup. Old request with a known command ID gets no receipt body, execution
   detail or stale-account acknowledgement. Current same-account clients can
   read new scope and retry the original intent/receipt under P01B rules; do not
   rewrite historical acknowledgement into current-plan authority. Preserve
   command uniqueness, atomic effects/reservations and CAS behavior.
8. Rotate/invalidate account-view scopes and every account-derived process cache
   or queued publication at these boundaries. Late old-account results cannot
   paint a current view. Public item/reference caches can survive if they carry
   no account data. Safe structured failures render French at presentation;
   no raw credential/account payload or migration diagnostic in responses/logs.
9. Persisted records remain account-scoped. Add only generation-related integrity
   checks necessary for admission/recovery; no retention-policy expansion,
   destructive data-clearing shortcut or compatibility-only migration. Document
   permitted dev DB recreation explicitly if needed; never delete credentials.

## Acceptance vectors

| Deterministic interleaving | Required result |
|---|---|
| Fetch on A; switch to B; release A response | No A commit/status/loop publication; B unchanged |
| A→B→A with old A token/command ID | Old scope rejected before receipt lookup |
| Clear while fetch is blocked; release it | Cleared data stays clear; no recreated profile/receipt |
| Restore older backup while fetch blocked | Restored incarnation fresh; old result cannot overwrite it |
| Valid/invalid restore, failure during replace/generation publish | Safe rollback or unavailable state; no half-ready state |
| Restart after clear/restore; stale browser retry | Stale scope rejected; new-scope valid receipt replay works |
| Credential changes during multi-page TP/crafting reads | Consistent captured bundle; stale/mixed commit rejected |
| Completion and transition/reconciliation race | Either valid atomic completion before boundary or rejection; no torn effects/receipt |
| Failure status from obsolete refresh | Cannot overwrite current account readiness |
| New context successful normal sync/command | Works; fence is not a blanket permanent block |
| Backup restore/recovery crash boundary | Old imported generation never exposed as admissible |

Use explicit barriers, not sleeps; real SQLite restart/restore and rollback tests.
Test public command/commit boundaries, not just generation helper comparison.
Add a pre-fix failing A→B→A or clear/late-write regression.

## Validation and exclusions

Focused new fence/transition tests plus Application TP synchronization/Crafting/
Plan tests, Infrastructure SQLite persistence/recovery tests and Web Plan/local
data/account-scope tests. Run frontend retry tests if scope payloads change,
node --test .github/scripts/*.test.mjs and required CI. Existing privacy/recovery
checks remain mandatory. Carry VERIFY-008/013/016/017; no upstream cache claim.

No live holdings integration, native companion, UI redesign, generic caching,
new public exposure, game writes, fee or retention policy change. Update #170's
batch row with exact reviewed head/CI/guard coverage. Commit [TKT-M22-P03A];
Draft PR develop, Closes #170, milestone 11. Green CI precedes fresh independent
GPT-6.1 Sol XHigh; Ready only after approval. Stop for owner merge. Next is #171.
