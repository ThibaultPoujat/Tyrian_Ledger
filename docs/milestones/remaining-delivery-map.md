# Remaining Delivery Map

Owner-approved rolling planning: 2026-09-29; B3 prepared 2026-10-02. Inspected
baseline: `develop ea4cc72e0240a9294074e1a6f77fa345daa0e416`, after B2.
This maps the complete remaining corrective program through release. The
[approved delivery plan](approved-delivery-plan.md) still owns package exits and
audit traceability. This document decomposes those packages; it does not claim
future work is implemented or authorize all work in one Goal.

## Planning precision and readiness

B1/B2 are merged, with exact review/final-head evidence in [B3](M22/batch-03.md).
B3 prepares four conditionally Ready backend contracts and one environment-gated
Windows probe. C04 is the owner-authorized Sol Medium checkpoint instead of Astra.
A ticket becomes executable when its named preparation/predecessors are merged,
required evidence exists and its assumptions still hold. Readiness is conditional,
not inferred from an open issue, a number or a generated preferred-ticket field.

Units explicitly promoted to B3 below have that manifest's conditional entry
checks. All other future work units are **Planned, not Ready**. Unnumbered future IDs are map labels; only explicit #issue contracts are
executable after their entry checks. At each batch checkpoint, Astra validates the delivered
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
| P02A / B2 / #168 | Typed all-character holdings collector with per-location/source provenance; disconnected from live persistence/admission | C03; B1 evidence frame; optional endpoint coverage | All actors retained, partial failures honest, fan-out bounded, delivery isolated | NORMAL GPT-6.1 Sol Medium |
| P02B / B2 / #169 | Equipped/template protection and actor-specific crafting policy; producer seam only | P02A; documented equipment/tab schema | No fabricated actor or protected input; uncertainty fails closed | NORMAL GPT-6.1 Sol High |
| P03A / B2 / #170 | Durable store incarnation and account/reset/restore generation; obsolete work cannot commit or retrieve receipts | P02A/B; P01B receipts; actual writer/publication inventory | Switch/clear/restore races, no old account write or receipt leakage, restart/rollback | Explicit GPT-6.1 Sol XHigh gate |
| P02C / B2 / #171 | Guarded persistent location/protection evidence and one conservative holdings projection | P03A; P02A/B seams | No portfolio/location/delivery double credit; protected/actor eligibility; Partial live physical proof | NORMAL GPT-6.1 Sol High |
| P06-A / B3 / #182, environment-gated | Early Windows tray/notification feasibility and packaging decision, with minimal executable probe | Existing loopback host; actual Windows environment and owner architecture decision if needed | UI closed/minimized, lock/sleep/resume, denied permission; truthful limits | NORMAL GPT-6.1 Sol High; production architecture remains owner-gated |
| P02-E / split below; production proof Planned | Action-relevant transfer/coherence evidence and supported equipment correspondence; improve multi-location admission only with adequate correlation, retain conservative fallback otherwise | P02C; VERIFY-016/017 research and actual endpoint evidence | Delayed bag↔bank↔delivery replays conserve resources; supported craft confirmation and exact correspondence have adequate proof; unsupported shapes remain explicit, never globally atomic claims | Strong evidence/resource review; assign gate at preparation |
| P01-E / after slice-specific action/basis proof, Planned | Settled vs merely reported executions; repeat same strategy with a new execution; idempotent starts | P01C/D; P02C/P02-E action-relevant evidence; P03A | Unconfirmed buy/list/fill cannot settle; settled strategy repeats; late evidence preserves history; start retry never starts twice | Explicit Sol gate expected |
| P01-F / B3 | Passive procurement becomes repriced feasible craft/sell continuation; collection distinguished from fill | P01-E; P02C/P02-E supported usable inventory/capabilities | Order→partial/full fill→collect→craft→sell; no duplicate acquisition; capacity/depth deterioration pauses | Strong lifecycle/economics review |
| P01-G / B3 | General residual replanning and explicit contradiction/undo recovery beyond P01D's one-item case | P01-E/F | Multi-input partial chain conserves inputs/output/basis; acted descendants not rewritten | Explicit Sol gate expected |
| P02D / Planned | Transformation/transfer cost provenance and outcome events across trade/craft chains | P02C/P02E2 supported action evidence; existing P01C/D report/reconciliation event seams, not P01-E settlement | Known/unknown basis preserved; no phantom TP holdings; fees/cash/profit separated | Strong accounting review |
| P02E1 / B3 / #181 | Bounded action-evidence investigation/export/replay; no production proof promotion | B2 source clocks; #180 | Actual-vs-synthetic matrix, privacy and failure replay, precise unresolved P02E2 predicates | NORMAL GPT-6.1 Sol High |
| P02E2 / Planned | Supported production correlation/confirmation and instance correspondence only with adequate evidence | P02E1 actual evidence and owner authority decisions where needed | Source-specific positive proof, uniqueness/replay/correction; conservative fallback otherwise | Assign explicit Sol gate at preparation |
| P03C1 / B3 / #180 | Local completion/undo using trusted bound context; zero upstream command dependency | P03A/B2; #179 | Scope-before-receipt, local durability under HTTP barriers, atomic consuming guards | Explicit GPT-6.1 Sol XHigh gate |
| P03C2 / Planned | Persisted decision read models and targeted fresh start/preflight | P03C1; P01 lifecycle | Stale commitments cannot start; coherent invalidation/restart and measured latency | Assign strong concurrency review at readiness |
| P03-B / B3 #178/#179 | Reusable endpoint-aware caches and bounded priority scheduler | P03A epochs; P02 source contract | Metadata reuse, coalescing, Retry-After handling, bounded request/concurrency policy; no cache proof fabrication | Sol High |
| P03-C / command slice #180; remainder Planned | Persisted/local decision read models, targeted start preflight and short command commit gates | P03-B; P01 lifecycle | Completion stays local/durable during unrelated API stalls; stale commitment blocked; latency measured | Strong concurrency review |
| P04-A / B4 | Versioned session preferences and common feasibility/economics for both objectives | P01/P02 integrated slice; P03 read models; approved preference semantics | Allocation/reserve/commitment examples; active time vs liquid cash deadline; unknown basis stays explicit | Strong financial review |
| P04-B / B4 | Bounded rotating research universe, cold-start path and observation-coverage quality | P03-B; P04-A contract | Candidate beyond old prefix eventually checked; clustered/stale history rejected; work bounded | Sol High |
| P04-C / B5 | Shared utility/ranking, worthwhile-action and attention gates across supported strategies | P04-A/B | Comparable economics/time; hard failures cannot be outranked; scarce urgent alerts | Strong recommendation review |
| P05-C / B5 | Connect the first four approved views to live typed services; normal API observation plus exceptional local report | P05A/B; P01–P04 required contracts | Same screenshot hierarchy with live adapters; no fixture fallback; restart/account-switch/retry integration | Sol High + visual |
| P05-D / B5 | Bilan and Réglages: outcome certainty, time, coverage, protection and local settings | P02D; P04 preferences; P06 notification status contract | 05/06 screenshots; unknown basis not profit; estimated time not measured; coverage and permissions truthful | Sol Medium/High + visual |
| P06-B / B5 | Durable important-alert queue, deduplication and native delivery with app UI closed | P06-A decision; P03 epochs; P04-C actionable reasons | Restart/resume/dismissal; no repeated non-action alerts; permissions diagnostic | Strong persistence/security review |
| P06-C / B6 | Windows install/update/uninstall, tray lifecycle and recovery integration | P06-B; live UI; all personal-data schemas | Clean owner/friend install; credential handling; stop/start/update/uninstall/restore evidence | Sol High |
| P07-A / B6 | Integrated scenario/fault replay and accessibility/performance evidence | All P01–P06 exits; shared P01/P02 integration proof | First vertical slice, partial/contradicted/passive cases, offline/rate-limit/sleep, all six screens | Sol High |
| #96 / B6 | Final security/recovery/E2E/release gate; implement only remaining bounded hardening gaps | P07-A plus #150 exit evidence and owner progression approval | Existing security/recovery ticket and clean installation evidence; unresolved blockers explicitly held | Existing Sol XHigh gate |
| #97 / B6 | Evaluate real supported plan outcomes and whether actions justify interruptions | #96; Bilan and provenance already working | Owner sessions, honestly attributed gain/cash/time; no automatic rule changes | Reassign at readiness from model guide |

Proposed batch groupings are capacity estimates, not commitments to implement a
large unit unsplit. At most 3–5 ready child contracts are promoted per planning
pass. Windows proof requires a Windows runner/device: Linux screenshots do not
prove native notification delivery. No Windows environment evidence was supplied
at B2 preparation. B3 provides the bounded probe contract #182 but access remains UNCONFIRMED.
It is not Ready until interactive Windows entry evidence exists. The environment
gate is raised now and blocks lifecycle expansion; never substitute
Linux screenshots for native Windows proof or defer discovery until release.

## Lifecycle and provenance dependency boundary
+
+There is no settlement↔basis cycle. P02D can extend already reported/reconciled
+P01C/D execution events and supported P02E2 facts; it does not require P01-E
+settlement or repetition. Future P01-E must identify its supported slice and
+required accounting proof. A bounded TP slice may use the existing ledger only
+if its acquisition/disposal provenance and reconciliation are actually adequate;
+this checkpoint does not assert that readiness. Craft/transfer settlement needs
+the relevant P02D proof. Unknown basis stays explicit, never fabricated to call
+an execution settled. Both units remain Planned until bounded entry contracts
+and evidence exist; all P01/P02 integrated exits are retained.
+
+## Shared architecture contracts

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

B1: implementation and CI/review evidence accepted; owner selected “Changes
needed” on 2026-10-01. Concrete feedback/usability acceptance remains open. The
four-screen fixture preview is not live runtime completion.

B2: inspect collected coverage, protections, generation fences and conservative
projection together. Do not claim coherent account proof or full P02 closure.
Prepare P02-E to own deferred coherence/correspondence proof, alongside the early
Windows probe and its environment/architecture gates;
retain owner UI feedback before P05C. Existing owner decision gates remain.

B3: reuse references, bound dispatch priorities, remove local command upstream
dependencies and investigate action evidence; run the Windows probe only with
actual environment access. P01-E/F/G and P02D remain Planned, dependent on P02E2
and required provenance. The first repeatable trade/craft lifecycle remains a
later integrated exit, not an assertion made by this batch. Controlled account
experiments require authorized read-only access and private local evidence; no
credentials/raw private payload in prompts/fixtures.

B4/B5: verify both session objectives and live screen behavior, then quiet normal
play sessions. Track whether advice earned its interruption and where UI confused.

B6: clean friend-install test, release/security acceptance and evidenced outcomes.
The owner declares release. 0.1 scope remains trading/crafting; future
conversions/salvage/events and investment discovery are not quietly added here.

## Audit coverage

P01A covers F01 and P01B supplies command replay safety. Remaining mappings:
F02/P02C/P02-E; F03/P01-F; F04/P01-E; F05/P01D then P01-G; F06/P05-C;
F07/P04-A/C; F08/P04-B; F09/P03-C; F10/P03-B; F11/P03A/B/C;
F12/P04-B; F13/P04-B; F14/P02D/P04-A; F15/P02D/P05-D;
F16/P06-A/B; F17/P02B/P02-E; F18/application contracts in each relevant ticket;
F19/all ticket vectors plus P07-A. The approved plan's exit criteria still apply.
