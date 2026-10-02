# TKT-M22-P03C1 — Local Completion and Undo Command Admission

GitHub issue: #180

M22; R3. Implementation GPT-6.1 Sol High.
SOL-GATED: fresh independent GPT-6.1 Sol XHigh after green required CI.
Conditionally Ready after #179 merges; verify B2 generation/receipt and
holdings SQLite seams remain guarded. This is the bounded command slice of P03-C.

## Outcome and gap

A displayed step can be reported or safely undone while unrelated ArenaNet reads
are stalled. CompleteAsync currently fetches account identity before receipt lookup;
UndoAsync invokes RequireContextAsync/BuildDecisionContextAsync, including account
and opportunity work. Local durability must not depend on those upstream paths.
Start and live decision read-model redesign remain later P03C2 work.

## Contract

1. Resolve command authority locally from the host-owned captured P03A context's
   already-bound AccountScope, credential session, generation and store incarnation.
   Browser token is only a match/check, never an account identity authority. Native
   credential observations and guarded SQLite access remain required. No browser
   supplied account ID or persisted credential fingerprint may replace this seam.
2. If current context has no verified bound scope (startup, credential transition,
   clear/restore or failed validation), reject locally with structured unavailable/
   scope-changed status before profile/receipt lookup. A separate background account
   refresh binds identity through the existing typed gateway. Never bind from an
   old database profile or delay the local command by an inline ArenaNet fetch.
3. Complete and Undo must perform zero ArenaNet/market/crafting/recommendation HTTP
   calls and no global candidate analysis. Extract command-only Application service
   seams where needed; Web remains thin. Retain current supported completion/
   correction/partial and undo semantics, event effects, prices and quantities.
4. Preserve scope-before-receipt, logical command identity, plan/step/revision CAS,
   account ownership, reservations and atomic event/state/receipt persistence.
   Undo uses the same trusted account/view checks and optimistic revision boundary;
   guided correction cannot reverse real game actions or erase acted descendants.
5. Consuming completion still revalidates the stored selected location, actor,
   protections, source ages, physical cap and global resources in the command
   transaction. Missing/stale/moved evidence blocks consumption; local speed never
   authorizes substitute characters, provisional gains or stale holdings.
6. No operation/generation/database lease is held across ArenaNet HTTP. Reporting
   against current stored evidence must complete while unrelated fetches are held
   at deterministic barriers. Preserve TP serialization and P03A lock order;
   an unrelated stalled fetch cannot hold the local commit's operation gate.
7. After Applied/Undo persistence, current-generation decision/notification
   invalidation survives request cancellation. Stale delayed invalidation cannot
   clear newer work. Failure after commit remains recoverable by receipt/revision
   replay, never a second effect. Response buffering still rejects obsolete data.
8. Use the existing shared clock abstraction for changed temporal decisions and
   measure local admission/commit/invalidation separately. Controlled local tests
   should meet a 500 ms completion/undo target on documented equipment, with
   zero upstream dependency; barrier ordering is the correctness assertion.

## Acceptance vectors

| Case | Required result |
|---|---|
| Unrelated account/market fetch blocked indefinitely | Report/eligible Undo commits before barrier release; zero command HTTP |
| No bound scope after startup/clear/switch | Local safe rejection before profile/known receipt lookup |
| A→B→A old browser token/known command | Rejected before lookup; no leak or cross-account effect |
| Double click/restart retry/concurrent revision | One receipt/effect; same acknowledgement or explicit conflict |
| Source moved/stale/protected or actor unavailable | No consuming effect or replacement location |
| Browser cancels immediately after durable Applied | Receipt persists; current invalidation completes |
| Clear/restore races with queued local command | Atomic winner or generation rejection; no torn publication |
| Undo after acted descendant / concurrent refresh | Preserved policy, conflict/pause; no deletion of supported history |
| Existing partial completion and NotPerformed | Unchanged bounded quantities/reservations/confirmation policy |

## Validation and exclusions

New actual-host network-barrier/zero-HTTP tests; PlanEndpointMapping/AccountWorkBoundary
and Application command/undo tests; real SQLite command/holdings/receipt/recovery CAS
vectors; frontend retry/undo tests and build if payloads change; workflow suite,
full backend and required CI. Record latency environment/samples/median/p95 and
explicit barrier evidence in PR. UI changes need original/actual 1920×1080 evidence.

No start-idempotency redesign, settlement/repetition, positive physical confirmation,
general residual replanning, passive continuation, decision persistence or fee policy
change. P03C2 retains targeted start/preflight/read-model work. Preserve VERIFY IDs.
Commit `[TKT-M22-P03C1]`; PR develop, Closes #180, milestone 11.
Draft until green CI and fresh Sol XHigh APPROVE; owner merges; stop.
