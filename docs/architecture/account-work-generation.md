# Account Work Generation Fence

TKT-M22-P03A / #170. The host registers one `IAccountWorkFence` through
`AddTyrianLedgerAccountWorkFence`. An application work context carries a trusted
`AccountScope`, opaque credential-session identity, store incarnation and fresh
generation. These are internal identities, never browser credentials or a
financial authority. The browser receives a separate random view token.

## Admission and transitions

```text
Startup -> validate/create nonsecret incarnation -> fresh generation -> Ready
Ready -> observe different credential/account -> invalidate caches -> fresh generation
Ready -> clear/restore -> Quiescing (old work invalid) -> storage operation
  -> valid live/restored data + durable incarnation publish -> fresh Ready
  -> failed replacement/publication/cancellation -> Unavailable until recovery/restart
Invalid uploaded backup -> preserve live data -> new incarnation/generation -> Ready
```

`RunAsync` observes the existing host credential source before admitting work.
Infrastructure keeps the captured credential only in memory, through its safe
key-source adapter. Every private request, decision loop, TP/crafting refresh and
opt-in holdings collection uses one captured credential for its authenticated
bundle, including pages, workers and retries. Binding an inconsistent account
invalidates the generation and rejects the bundle. Public request identities
include opaque generation IDs; no key/hash/fingerprint is persisted or exposed.

Native-store observations are serialized. Before every private database lease or
publication, the adapter observes the current source again and checks the context
under the generation semaphore. External OS-store changes are detectable only
when observed on a read; an unobserved A→B→A cannot be claimed detectable.
Cancellation alone is not the authority.

The nonsecret `<database>.incarnation` sidecar contains one random GUID. It is
written through a flushed temporary file and atomic rename. It deliberately lives
outside imported SQLite backups: replacing a database cannot import its browser
admission identity. Clear and successful/invalid restore rotate it before Ready;
every host startup also creates a fresh in-memory generation. A malformed or
unwritable incarnation fails closed. Publication failure after a valid database
replacement leaves private operations unavailable; restart validates storage and
uses a new generation. Existing staged validation, pre-restore backups, atomic
database replacement and artifact cleanup remain in force.

No SQLite schema change or preservation-only migration is introduced. Existing
development databases need no recreation for this ticket. Any later explicit dev
recreation must follow the pre-0.1 policy; this change never deletes credentials.

## Lock order and actual coverage

Capture/observe, asynchronous fetch and commit are separate phases. No generation
lease is held during ArenaNet HTTP or native credential reads. Private persistence
uses **generation → database**; recovery uses **generation → operation → database**.
Backup/cleanup use operation → database without acquiring generation. A private
write never obtains operation before generation. SQL ownership checks and mutation
or receipt lookup share the database/generation lease; existing transactions and
CAS remain authoritative. Recovery holds quiescence through validation/replacement
and incarnation publication. Failed recovery cannot fall back to an unguarded gate.

| Producer / writer / publication | Admission and guard |
|---|---|
| TP synchronization success and failure status | Service bundle; `SqlitePersonalTradingPostSynchronizationStore.AcquirePrivateAsync`, target account check, existing atomic transaction |
| Account profile, TP transactions/current orders/observations | `SqlitePersonalTradingPostRepository.AcquirePrivateAsync` plus captured account check |
| Crafting snapshot reads/replacement | Service bundle; `SqliteAccountCraftingSnapshotRepository.AcquirePrivateAsync`, snapshot account check |
| Plan start, refresh, reconciliation, undo | Private request bundle; `SqlitePlanRepository.AcquirePrivateAsync`, profile ownership SQL before mutation/CAS |
| Completion effects, reservations and receipts/replay | Browser view scope checked before profile/receipt lookup; same private repository guard/profile check precedes receipt SQL and atomic command transaction |
| Investment position/target/exit writes and reads | Private request bundle; `SqliteInvestmentPositionRepository.AcquirePrivateAsync` and account check |
| Decision loop ready/degraded/failure status, plans projection and notification observation | Loop bundle; explicit publication lease; projection generation also guards local plan mutations |
| Notification preferences/acknowledgements | Request bundle; explicit publication lease around ledger mutation |
| Private API read models and acknowledgement bodies | Middleware buffers the private body and validates a publication lease before copying it to the response; rejected work returns only safe `account_scope_changed` with French copy |
| Market sampling from private orders/investments | Collector bundle; private repository reads guarded; membership health tagged by captured generation; public price evidence remains reusable |
| P02A/B opt-in holdings collector | Bundle, trusted account binding and final publication lease; no new polling, persistent projection or resource admission |
| Clear/restore | Quiescing generation lease before operation/database leases; fresh incarnation before Ready |

All five private repository families use the shared production database gate.
Absent captured context rejects production private access. Optional constructor
seams remain for existing isolated tests; production composition always registers
the concrete fence and startup initializer. Initialization failure stops the host.

Transitions clear browser token entries, plans projections, queued loop projections,
loop status/recommendations and the notification ledger. Connection-validation
cache and collector membership health carry generation tags, so obsolete results
cannot become current cache entries. Gateway scheduler keys isolate private
generations. Public reference/item/price caches and persisted account records can
survive; they confer no old-generation admission. Clear retains its existing
explicit deletion semantics.

## Acceptance evidence and handoff

`AccountWorkGenerationIntegrationTests` uses barriers and real SQLite for switch,
clear, older-backup restore, invalid restore, failed replacement, failed generation
publication, restart, mixed targets, private admission, atomic completion and
receipt replay. Real typed HTTP gateways exercise two TP pages and crafting/holdings
bundles with a credential replacement while fetches are blocked; every request
keeps the captured credential, while old success/failure writes are rejected.

`AccountWorkBoundaryTests` checks A→B→A scope-before-known-command lookup, private
response suppression and obsolete loop failure. Its TestServer case uses the actual
backup/clear/restore endpoints, then restarts against the same SQLite store and
rejects each earlier browser scope. The ABA test fails with the original per-account
token behavior, which reaches the forbidden profile lookup; the generation-aware
token implementation passes. Infrastructure restart replay still acknowledges the
original receipt under a newly admitted same-account context without inventing
current-plan authority.

#171 must use this existing fence/private database seam for persistent protected
holdings. This ticket adds no live holdings integration, scheduler/cache redesign,
UI redesign, new key grants, retention/fee policy, network exposure or gameplay
writes. VERIFY-008/013/016/017 remain OPEN; no upstream cache/coherence or live
authenticated acceptance claim is made. No frontend layout/copy is changed, so a
new visual preview is not applicable.
