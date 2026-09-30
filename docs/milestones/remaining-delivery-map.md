# Remaining Delivery Map

Owner-approved rolling planning: 2026-09-29. Inspected baseline:
`develop e9712b4e97b547550ed73f33681af3a81be76d39`, after P01B.
This maps the complete remaining corrective program through release. The
[approved delivery plan](approved-delivery-plan.md) still owns package exits and
audit traceability. This document decomposes those packages; it does not claim
future work is implemented or authorize all work in one Goal.

## Planning precision and readiness

Only the four [B1 contracts](M22/batch-01.md) below are fully specified now.
A ticket becomes executable when its named preparation/predecessors are merged,
required evidence exists and its assumptions still hold. Readiness is conditional,
not inferred from an open issue, a number or a generated preferred-ticket field.

Later work units below are **Planned, not Ready**. IDs such as P02-A are map labels,
not GitHub issues. At each batch checkpoint, Astra validates the delivered
interfaces and promotes the next 3–5 units to bounded contracts. Split a future
unit if it cannot produce one coherent, independently reviewable PR. Do not
prewrite file-by-file recipes for code that does not yet exist.

Review column is an intended minimum for future planning, not an active gate.
The model guide remains the sole active model/gate authority. High-consequence
ambiguity, new durable authorities or changed invariants require explicit
review assignment before coding; quota does not waive review.

## Delivery dependency map

| Unit / proposed batch | Outcome and boundary | Prerequisites | Proof at exit | Intended review |
|---|---|---|---|---|
| P01C / B1 / #158 | Source-scoped evidence, conservative incompleteness and stable reconciliation progress | Merged P01B; C02 | Old TP refresh cannot contradict stale inventory; replay/CAS tests | Sol XHigh gate |
| P01D / B1 / #159 | Actual partial quantities; safe bounded residual exit; unsupported chains pause | P01C | Partial 4/10 never produces a 10-unit dependent instruction | Sol High NORMAL |
| P05A / B1 / #160 | Shared React shell, Signaux and session drawer with fixtures | P01D in queue; six-image baseline | 01/04 screenshot and interaction evidence; no live mutation | Luna High NORMAL |
| P05B / B1 / #161 | Plan alternatives and grouped execution/recovery preview | P05A | 02/03 comparisons; long/pending/contradicted plan interactions | Sol Medium NORMAL |
| P02-A / B2 | Complete supported physical holdings: all character inventories, bank, materials, delivery and provenance; transfers do not duplicate | P01C evidence frame; verified endpoint/permission coverage | Per-source coverage; bag↔bank↔delivery move replay, partial-source failure | Strong account/resource review |
| P02-B / B2 | Equipped/template-instance protection, explicit protected materials and actor-specific crafting capability | P02-A; verified equipment/template and character contracts | Protected instances excluded; chosen character can perform every craft; unavailable coverage fails closed | Strong protection/security review |
| P03-A / B2 | Durable account/reset/restore generation; obsolete work cannot commit | P02-A source ownership; P01B receipts | Switch/clear/restore while reads run; no old account write or receipt leakage; restart | Explicit Sol gate expected |
| P06-A / B2 | Early Windows tray/notification feasibility and packaging decision, with minimal executable probe | Existing loopback host; target Windows environment | UI closed, minimized, lock/sleep/resume, permission denied; record supported delivery limits | Sol Medium; security escalation if needed |
| P01-E / B3 | Settled vs merely reported executions; repeat same strategy with a new execution; idempotent starts | P01C/D; P02-A/B; P03-A | Unconfirmed buy/list/fill cannot settle; settled strategy repeats; late evidence preserves history; start retry never starts twice | Explicit Sol gate expected |
| P01-F / B3 | Passive procurement becomes repriced feasible craft/sell continuation; collection distinguished from fill | P01-E; complete usable inventory/capabilities | Order→partial/full fill→collect→craft→sell; no duplicate acquisition; capacity/depth deterioration pauses | Strong lifecycle/economics review |
| P01-G / B3 | General residual replanning and explicit contradiction/undo recovery beyond P01D's one-item case | P01-E/F | Multi-input partial chain conserves inputs/output/basis; acted descendants not rewritten | Explicit Sol gate expected |
| P02-C / B3 | Transformation/transfer cost provenance and outcome events across trade/craft chains | Complete account evidence; P01 lifecycle | Known/unknown basis preserved; no phantom TP holdings; fees/cash/profit separated | Strong accounting review |
| P03-B / B4 | Reusable endpoint-aware caches and bounded priority scheduler | P03-A epochs; P02 source contract | Metadata reuse, coalescing, Retry-After handling, bounded request/concurrency policy; no cache proof fabrication | Sol High |
| P03-C / B4 | Persisted/local decision read models, targeted start preflight and short command commit gates | P03-B; P01 lifecycle | Completion stays local/durable during unrelated API stalls; stale commitment blocked; latency measured | Strong concurrency review |
| P04-A / B4 | Versioned session preferences and common feasibility/economics for both objectives | P01/P02 integrated slice; P03 read models; approved preference semantics | Allocation/reserve/commitment examples; active time vs liquid cash deadline; unknown basis stays explicit | Strong financial review |
| P04-B / B4 | Bounded rotating research universe, cold-start path and observation-coverage quality | P03-B; P04-A contract | Candidate beyond old prefix eventually checked; clustered/stale history rejected; work bounded | Sol High |
| P04-C / B5 | Shared utility/ranking, worthwhile-action and attention gates across supported strategies | P04-A/B | Comparable economics/time; hard failures cannot be outranked; scarce urgent alerts | Strong recommendation review |
| P05-C / B5 | Connect the first four approved views to live typed services; normal API observation plus exceptional local report | P05A/B; P01–P04 required contracts | Same screenshot hierarchy with live adapters; no fixture fallback; restart/account-switch/retry integration | Sol High + visual |
| P05-D / B5 | Bilan and Réglages: outcome certainty, time, coverage, protection and local settings | P02-C; P04 preferences; P06 notification status contract | 05/06 screenshots; unknown basis not profit; estimated time not measured; coverage and permissions truthful | Sol Medium/High + visual |
| P06-B / B5 | Durable important-alert queue, deduplication and native delivery with app UI closed | P06-A decision; P03 epochs; P04-C actionable reasons | Restart/resume/dismissal; no repeated non-action alerts; permissions diagnostic | Strong persistence/security review |
| P06-C / B6 | Windows install/update/uninstall, tray lifecycle and recovery integration | P06-B; live UI; all personal-data schemas | Clean owner/friend install; credential handling; stop/start/update/uninstall/restore evidence | Sol High |
| P07-A / B6 | Integrated scenario/fault replay and accessibility/performance evidence | All P01–P06 exits; shared P01/P02 integration proof | First vertical slice, partial/contradicted/passive cases, offline/rate-limit/sleep, all six screens | Sol High |
| #96 / B6 | Final security/recovery/E2E/release gate; implement only remaining bounded hardening gaps | P07-A plus #150 exit evidence and owner progression approval | Existing security/recovery ticket and clean installation evidence; unresolved blockers explicitly held | Existing Sol XHigh gate |
| #97 / B6 | Evaluate real supported plan outcomes and whether actions justify interruptions | #96; Bilan and provenance already working | Owner sessions, honestly attributed gain/cash/time; no automatic rule changes | Reassign at readiness from model guide |

Proposed batch groupings are capacity estimates, not commitments to implement a
large unit unsplit. At most 3–5 ready child contracts are promoted per planning
pass. Windows proof requires a Windows runner/device: Linux screenshots do not
prove native notification delivery. Surface that dependency at B1's checkpoint
so the feasibility work is not deferred until release.

## Shared architecture contracts

- Application owns deterministic integer-copper policy; Web maps transport,
  Infrastructure owns persistence/typed gateways; React renders structured data.
- Evidence separates account scope, source coverage, observation/capture and
  uncertainty. Complete inventory cannot be inferred from completed TP sync.
- Command identity, optimistic revision and atomic receipt/state admission survive
  replanning/reconciliation/undo. No new gameplay automation.
- Provisional outputs remain reserved to their safe continuation; confirmed
  evidence retires only corresponding shadow effects. P01/P02 exit together.
- Session preferences define allocation base, untouched reserve, accepted loss,
  lock horizon, total gain, active-rate threshold and activity/time objectives.
  Changing preferences never erases existing commitments.
- Read models carry exact instructions, eligibility/reasons, provenance/uncertainty,
  active vs waiting time and cash vs profit. A future API adapter cannot silently
  narrow these semantics to fit the fixture view model.
- API cache/fee/permission assumptions remain in VERIFY until actually evidenced.
  Do not assert a five-minute guarantee or treat mocked endpoint data as validation.
- The latest six approved PNGs own appearance. Fixture previews become reusable
  application components; P05-C replaces providers, not the accepted design.
- Completion of a code ticket is not owner usability acceptance or release consent.

## Checkpoints and owner involvement

B1: owner tries four fixture-backed screens; Astra checks #158–#161 evidence and
the next account/epoch interfaces, then prepares B2. No runtime completion claim.

B2: account coverage/protections and Windows feasibility are evidenced; decide any
real platform/permission gap before the next contracts. Existing owner decision
gates remain unchanged.

B3: run the first repeatable trade/craft lifecycle with conservation and recovery
proof. Begin controlled owner-account validation only with complete protections
and authorized read-only access; no exposed credentials in prompts/fixtures.

B4/B5: verify both session objectives and live screen behavior, then quiet normal
play sessions. Track whether advice earned its interruption and where UI confused.

B6: clean friend-install test, release/security acceptance and evidenced outcomes.
The owner declares release. 0.1 scope remains trading/crafting; future
conversions/salvage/events and investment discovery are not quietly added here.

## Audit coverage

P01A covers F01 and P01B supplies command replay safety. Remaining mappings:
F02/P02-A; F03/P01-F; F04/P01-E; F05/P01D then P01-G; F06/P05-C;
F07/P04-A/C; F08/P04-B; F09/P03-C; F10/P03-B; F11/P03-A/B/C;
F12/P04-B; F13/P04-B; F14/P02-C/P04-A; F15/P02-C/P05-D;
F16/P06-A/B; F17/P02-B; F18/application contracts in each relevant ticket;
F19/all ticket vectors plus P07-A. The approved plan's exit criteria still apply.
