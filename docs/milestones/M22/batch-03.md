# B3 — Collection Reuse, Local Commands and Evidence Investigation

Prepared 2026-10-02 at the owner's request, using the current GPT-6.1 Sol Medium
session instead of Astra for this checkpoint only. Inspected develop:
`ea4cc72e0240a9294074e1a6f77fa345daa0e416`. C04 / #177 is the preparation;
merge it before #178. Independent preparation review is GPT-6.1 Sol High.

## Prepared queue and entry checks

| Order | Contract | Entry evidence | Implementation / review |
|---|---|---|---|
| 1 | [P03B1 / #178](tickets/TKT-M22-P03B1.md) — bounded public reference cache | #177 merged; B2 guarded collector/projector and current typed gateway/scheduler tests | GPT-6.1 Sol High / NORMAL GPT-6.1 Sol High |
| 2 | [P03B2 / #179](tickets/TKT-M22-P03B2.md) — bounded request priority/fairness | #178 merged; cache/cancellation/request-count evidence | GPT-6.1 Sol High / NORMAL GPT-6.1 Sol High |
| 3 | [P03C1 / #180](tickets/TKT-M22-P03C1.md) — local completion/undo admission | #179 merged; current host-bound account/generation, atomic receipt/holdings seams | GPT-6.1 Sol High / SOL-GATED fresh GPT-6.1 Sol XHigh after green CI |
| 4 | [P02E1 / #181](tickets/TKT-M22-P02E1.md) — action-evidence investigation/replay harness | #180 merged with final-head CI/fresh approval; independent source clocks/Partial frame | GPT-6.1 Sol High / NORMAL GPT-6.1 Sol High |
| 5 | [P06A / #182](tickets/TKT-M22-P06A.md) — early Windows feasibility probe | #181 merged **and actual interactive Windows access confirmed**; presently ENVIRONMENT-GATED | GPT-6.1 Sol High / NORMAL GPT-6.1 Sol High |
| Stop | [G01 / #150](tickets/TKT-M22-G01.md) — integration checkpoint | Delivered contracts and unresolved environment/evidence/owner decisions | Planning checkpoint, never executable as one coding ticket |

The first four are conditionally Ready after named preparation/predecessors;
#182 is prepared but NOT Ready without its Windows environment. An open/preferred
issue is not executable permission. At #182, stop with the exact entry blocker
if access is still absent; no non-blocking alternate or skip is authorized.
The environment question is raised at this checkpoint, before any lifecycle
expansion. Windows proof is not deferred to release or replaced by Linux CI.

One implementation per Goal, independent review and owner merge. Verify exact
predecessor merges, reviewed runtime head, affected re-reviews and final-head CI.
Evidence-only changes after review require a diff/evidence check; changed runtime
requires affected review. No autonomous next-ticket run or merge.

## B2 delivery evidence

All four PRs are MERGED into develop, issue-closing and actual milestone 11.
Each exact final head has the five required CI jobs SUCCESS. Agent approvals
are independent review evidence, not author-submitted GitHub formal approvals.

| Ticket / PR | Merge commit | Reviewed scope / final head / required CI |
|---|---|---|
| #168 / [#173](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/173) | `4ac26cd7cde65b4386e0879d6b4e7b64f985b02f` | [Sol Medium APPROVE](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/173#issuecomment-5933277063) at runtime `b3adca9bd2be1bb4c55f1b4af8e0fb83ea344c3e`; final `08f165a072d3230f450f77bc8545b8254ebb545b` adds only #168 batch evidence. [Final CI 36874961720](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36874961720). |
| #169 / [#174](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/174) | `cf28b80061aa4f7f7f5835baab94e8bf98250616` | [Sol High APPROVE](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/174#issuecomment-5934783005) at exact final `abee12d204f9eb8a34bbb5da8a06bafdf54b7841`; runtime `4f71aa25414a94fd9737e1483bac84019e643691` followed only by reviewed tests/visual/docs evidence. [Final CI 36884928646](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36884928646). |
| #170 / [#175](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/175) | `520330d92d2e19e0bd656d96797336fb56e0fba1` | Later runtime fixes each received affected fresh review; [final Sol XHigh APPROVE](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/175#issuecomment-5937703335) at `0bf99e5d3b17b04731e573cf5537156a3dc97967`, identical final head. [Final CI 36904180430](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36904180430). |
| #171 / [#176](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/176) | `aaf3b6e16abb4ea5da2881991500150f3f043f08` | [Sol High APPROVE and targeted final addendum](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/176#issuecomment-5941651585) at final `dafbacfa7888fd6e6a71ce4b960bea2cdb6d1ab4`; runtime `4285d4b1cd87388b552cffc1f6d67717255b5afa`, visual evidence `e26bf7b238bcd2a7607ed24d9917a049d6bc523c`, later delta only #171 batch row. [Final CI 36933194834](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36933194834). |

Checkpoint independently inspected production collector/refresh/projector,
generation/command seams, scheduler and normalized holdings tests. At the baseline,
`dotnet test TyrianLedger.slnx -c Release --no-build --filter 'FullyQualifiedName~AccountHoldingsProjectionTests|FullyQualifiedName~AccountHoldingsPersistenceTests|FullyQualifiedName~AccountWorkBoundaryTests|FullyQualifiedName~PlanEndpointMappingTests|FullyQualifiedName~Gw2RequestSchedulerTests' --verbosity quiet`
passed **72** tests (12 Application, 35 Infrastructure, 25 Web). This is integrated
boundary evidence, not authenticated transfer proof or a fresh full financial audit.
The initial sandbox run could not open test communication sockets; the permitted
run passed. B2 PR reports retain full suites; this checkpoint does not relabel
those historical results as locally rerun.

## Package exits and retained gaps

| Package | Established boundary | Remaining exit / owning work |
|---|---|---|
| P00 | Approved product/visual authority and deterministic handoff | No change to scope; maintain matching live authorities |
| P01 | Aggregated resources, atomic reports/receipts, source-scoped conservative reconciliation and bounded partial exits | Settlement/repetition P01-E, passive continuation P01-F, general residual recovery P01-G; no end-to-end repeatable slice yet |
| P02 | All-character bounded collector, protections/real actors, generation-fenced SQLite, one conservative location and no delivery/portfolio double credit | Action/transfer/craft/correspondence proof P02E1→P02E2; transfer/transformation basis P02D; protection ambiguity remains conservative |
| P03 | Generation fences, existing in-flight coalescing/rate/concurrency bounds and loop projection | P03B1 reusable references, P03B2 priority/fairness, P03C1 local reports; P03C2 persisted read models/targeted start remains Planned |
| P04 | Existing deterministic trading/crafting engines | Versioned session/dual-objective economics, rotating research/coverage quality and common utility P04-A/B/C remain Planned |
| P05 | Four-screen fixture preview and scoped French actor/access/pause states | Live provider/shell convergence P05-C; Bilan/Réglages P05-D; six-screen and owner usability acceptance remain open |
| P06 | Loopback host and browser-dependent notification ledger | Actual Windows probe #182, then owner architecture choice, durable native alerts P06-B and install/update/uninstall P06-C |
| P07 | Meaningful package regressions and CI | Full vertical slice/fault/accessibility/performance, owner-account and friend-install acceptance P07-A, then #96/#97 |

P01/P02 integration is OPEN. A captured endpoint/complete roster is not a coherent
physical frame. Positive provisional gains cannot currently authorize a dependent
consuming instruction; this safety limit is real and active-work timing must not
hide it. P02E1 only prepares investigation; P02E2 must establish and review any
supported production correlation. P01-E must wait for that proof and slice-specific
accounting provenance; no settlement shortcut is authorized by this batch. P02D
uses existing P01C/D report/reconciliation seams and does not depend on settlement.
A future bounded TP settlement slice may use existing ledger proof if adequate;
craft/transfer settlement requires the relevant P02D proof. See the
[dependency boundary](../remaining-delivery-map.md#lifecycle-and-provenance-dependency-boundary).

## Visual, external and owner evidence

Opened approved original 01/02/03 and actual P02C bank, protected and moved-source
pause captures. The actuals expose actor/access and blocked controls in French;
they retain the 240px legacy rail, Artisanat navigation, green-black palette,
serif headings and wide cards instead of the approved navy/session-strip/grouped
design. [P02C reproduction and comparison](../../ux/evidence/TKT-M22-P02C/README.md)
and [P02B evidence](../../ux/evidence/TKT-M22-P02B/README.md) remain synthetic.
No new visual exception or owner approval is implied. Owner selected “Changes
needed” on 2026-10-01; concrete feedback is still pending. Request it before P05-C.

VERIFY-008/013/016/017 remain OPEN; no new upstream fact is resolved here.
#181 separates official documentation, synthetic replay, actual read-only captures
and unknowns. No API key or private/raw account payload may enter exports/prompts.
No new mandatory permission, fee/retention policy, network exposure, runtime LLM,
game action, paid service or production dependency is authorized.

Interactive Windows access is UNCONFIRMED at preparation. #182 requires an actual
device/session; build-only Windows CI cannot prove native delivery, lock or resume.
The probe assesses .NET tray/native notification and packaged/unpackaged constraints
using primary Microsoft documentation, then seeks owner approval for any production
architecture/dependency change. No app installation/autostart/release is approved here.

## Evidence handoff

Each implementation author updates only their assigned row, with PR, reviewed
runtime/evidence head, exact-final-head CI, focused vectors and preview/limitations.

| Ticket | Evidence PR / reviewed head / CI / handoff |
|---|---|
| #178 | [PR #184](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/184). NORMAL independent GPT-6.1 Sol High APPROVE at runtime `5c067d1d08998f48022415fd8ea3abe1fec35192`, plus the targeted public endpoint-matrix correction; no remaining findings. [Code-head CI 36972354217](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36972354217): all five required jobs SUCCESS. Later delivery delta contains only that documentation correction and this row; exact delivery head/final CI and targeted approval are recorded in the PR ([checks](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/184/checks)); preserve Draft until green. 168 focused Infrastructure tests, 845 full backend and 27 workflow tests pass; reviewer independently reran 168/27. Sequential/overlap/reordered reuse, schema/language isolation, TTL/rollback, capacity, partial/404/malformed recovery, detached recipes and waiter cancellation pass. Real holdings/crafting consumers share references with fewer reads, identical safe outcomes and real SQLite account/clear/restore rejection. API-only: no visual preview or frontend payload change. [Configuration/aggregate diagnostics/reproduction](../../architecture/architecture.md#13-public-reference-reuse-p03b1). VERIFY-008/013/016/017 remain OPEN; no private/commerce completed cache or upstream freshness claim. |
| #179 | Pending. Required: dispatch priority/fairness, promotion/coalescing, finite queues/Retry-After and account-bound queued cancellation. |
| #180 | Pending. Required: actual-host zero-HTTP/barrier latency, real SQLite scope/receipt/CAS and fresh Sol XHigh approval after green CI. |
| #181 | Pending. Required: replay/privacy harness, actual-vs-synthetic source matrix and precise P02E2 proof gaps. No production correlation relaxation. |
| #182 | ENVIRONMENT-GATED. Required: interactive Windows version/device, actual native delivery/lock/sleep/quit/relaunch/click matrix, cleanup and owner architecture handoff. |

## Exit and escalation

Stop at #182's exact entry blocker if Windows is unavailable, otherwise at #150
after its delivery. #150 remains open through P01–P06 exits and owner progression
acceptance. Next checkpoint prepares P02E2/required basis and lifecycle slices
only from adequate evidence; retain P03C2 and all later package scope in the map.
Two failed attempts at the same blocker or a genuine authority/owner decision
requires an evidence-rich handoff, not scope expansion or automatic next-ticket work.
