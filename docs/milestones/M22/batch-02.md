# B2 — Account Evidence, Protections and Epochs

Prepared 2026-10-01 by owner request after B1. Inspected baseline:
`develop 020ca25dae4d44a804e060cdda48616b795d51e8`.
C03 / #167 is this preparation; merge it before starting #168.
Owner-approved model update 2026-10-01: all four implementations use
`gpt-6.1-sol`, Medium for #168 and High for #169–#171. Review remains independent,
including fresh GPT-6.1 Sol XHigh after green CI for #170.
One ticket/Goal/PR, independent review and owner merge remain mandatory.

## Prepared queue and entry checks

| Order | Contract | Entry evidence | Implementation / review |
|---|---|---|---|
| 1 | [P02A / #168](tickets/TKT-M22-P02A.md) — location/actor-scoped holdings collector | #167 preparation merged; B1 evidence below | GPT-6.1 Sol Medium / NORMAL GPT-6.1 Sol Medium |
| 2 | [P02B / #169](tickets/TKT-M22-P02B.md) — equipment protection and real crafting actors | #168 merged; typed coverage/location seam and bounded gateway tests | GPT-6.1 Sol High / NORMAL GPT-6.1 Sol High |
| 3 | [P03A / #170](tickets/TKT-M22-P03A.md) — account/store generation fences | #169 merged; collector/protection handoffs; current private-writer inventory | GPT-6.1 Sol High / SOL-GATED fresh GPT-6.1 Sol XHigh |
| 4 | [P02C / #171](tickets/TKT-M22-P02C.md) — persistent protected projection/admission | #170 merged with final-head CI/fresh approval; guarded commit seam | GPT-6.1 Sol High / NORMAL GPT-6.1 Sol High |
| Stop | [G01 / #150](tickets/TKT-M22-G01.md) — batch/integration checkpoint | Four deliveries and integrated failure evidence | Astra planning, not an executable coding Goal |

Each new Goal verifies the exact predecessor issue/merged PR, reviewed code head,
final-head CI and that subsequent commits only add reviewed evidence. If runtime
code changed after review, obtain affected re-review before trusting it. Verify
typed seams and actual acceptance evidence; a closed issue or green CURRENT field
does not prove readiness. Do not implement successors in the same session.

P02A/B create a disconnected producer/policy seam. P03A fences existing private
work before P02C publishes new persistent holdings. This split replaces B2's two
oversized account units and postpones the Windows probe to the next preparation.
It changes decomposition, not P01–P07 scope or the Windows product target.

No new mandatory key permission, fee/retention policy, network exposure, runtime
LLM, or automatic game action is authorized. Missing optional source permission
degrades that source; it never triggers an automatic key change.

## B1 checkpoint evidence

All four issues are CLOSED and their PRs are merged into develop. The following
final heads have all five required CI jobs successful. Git diffs from reviewed
implementation head to final head contain only the assigned batch evidence row;
#166 also corrects two lines in its evidence README. No runtime code changed.
Recorded agent approvals are review evidence, not formal GitHub review submissions.

| Contract / PR | Merge commit | Reviewed implementation / recorded review | Final head / CI |
|---|---|---|---|
| #158 / [#163](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/163) | `95ccd06b311b10fc9f6ac7ac3613e9b3165216d8` | `cdbd6807e1818374908a8278df93f46fb9b6bee1`; [Sol XHigh APPROVE](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/163#issuecomment-5925897424) | `095b84424e923dee86c20f256ae458092f96483d`; [36824299955](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36824299955) |
| #159 / [#164](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/164) | `40cc25cf7d98c292cf6c7f478e7a422e54e06456` | `dd2b93ef4757a0949958d6c099c69ede01fb8ec6`; [Sol High APPROVE and final handoff extension](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/164#issuecomment-5926463915) | `22129400fc3a6521553adfceac8604a309fc5c37`; [36828576661](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36828576661) |
| #160 / [#165](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/165) | `2cd0b2bed5b5961b9df4576fb521a2e5266c06dd` | `9451046af501dff2c77e5a4d662ae6df2e3809d3`; NORMAL Luna High targeted APPROVE recorded in PR body | `5fecb88859b0e6a380199a40da11bb7de3b448b7`; [36835271491](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36835271491) |
| #161 / [#166](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/166) | `72ba3e87ca22d4ff78d05e246f245e19fc9ae368` | `ddbbf66a1e762d29a635fceb81b230696f5cc132`; [Sol Medium APPROVE including final evidence head](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/166#issuecomment-5928400063) | `8ff13c00271798c0d46c98a0a0cd0b72c206e9ef`; [36841453686](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36841453686) |

P01C/D deliver source-specific reconciliation and bounded partial quantities;
they do not prove settled/repeatable plans or complete live inventory.
VERIFY-008/013 remain OPEN. B1 CI and its meaningful regression vectors were
inspected; this planning checkpoint does not claim to rerun every financial test.

## Owner UI feedback and retained visual baseline

The owner selected **“Changes needed”** at this checkpoint on 2026-10-01.
Specific requested adjustments have not yet been supplied. UI usability acceptance
is OPEN. Merges, screenshots and independent code reviews are not owner approval.
Collect concrete feedback before the next UI contract/P05C; preserve the latest
six originals and do not invent a replacement design in this backend batch.

Original 01/04 and P05A urgent/drawer actuals, and original 02/03 and P05B
comparison/active actuals were opened for this checkpoint. Navy/gold shell,
compact signals, drawer, comparison and grouped execution exist; larger text/
vector illustration differences and simulation copy remain visible. This records
inspection, not a new visual exception or owner fidelity approval.
See [P05A evidence](../../ux/evidence/TKT-M22-P05A/README.md) and
[P05B combined preview](../../ux/evidence/TKT-M22-P05B/README.md).
All are fixture-only; Bilan/Réglages and live screen adapters remain later work.

## External endpoint and uncertainty handoff

Sources below were retrieved from ArenaNet's official wiki on 2026-10-01. They
define documented shapes, not an authenticated acceptance run or atomic snapshot.
Use the existing pinned gateway schema; validate all new DTOs against that version.

| Source | Documented shape / scope | B2 use |
|---|---|---|
| [characters](https://wiki.guildwars2.com/wiki/API:2/characters) | Name roster; account, characters | Complete expected actor set; private local names |
| [character inventory](https://wiki-en.guildwars2.com/wiki/API:2/characters/:id/inventory) | Bags with nullable slots; account, characters, inventories | Bag/slot observations, binding, attached components |
| [shared inventory](https://wiki.guildwars2.com/wiki/API:2/account/inventory) | Nullable account slots; account, inventories | Distinct shared-location observations |
| Bank/materials | Existing typed gateway and normalization | Preserve positions/source provenance, never add legacy portfolio twice |
| [delivery](https://wiki.guildwars2.com/wiki/API:2/commerce/delivery) | Coins and item rows; account, tradingpost; repeated IDs illustrated | Separate uncollected items/cash |
| [character crafting](https://wiki.guildwars2.com/wiki/API:2/characters/:id/crafting) | Discipline/rating/active per actor; account, characters | Preserve tuples; no fabricated aggregate actor |
| [equipment](https://wiki.guildwars2.com/wiki/API:2/characters/:id/equipment) and [tabs](https://wiki.guildwars2.com/wiki/API:2/characters/:id/equipmenttabs) | Equipment locations and all-tab query; scope documentation has an infobox/notes discrepancy | Protect every documented/unknown equipment location; optional grants only |

VERIFY-016 owns permission/schema/instance correspondence uncertainty (especially
equipment tabs). VERIFY-017 owns cross-endpoint/cache transfer coherence. Never
infer immutable instance identity from item ID/slot, or coherent physical proof
from two fetches separated by five minutes. P02C's conservative cap deliberately
may underuse holdings. Planned P02-E owns deferred action-relevant coherence,
transfer and supported instance-correspondence proof before dependent lifecycle
acceptance; it is not Ready and cannot promise a globally atomic API snapshot.

## Evidence handoff

Each implementation author updates only their row and links concise PR evidence.
Record reviewed head and final-head checks separately when bookkeeping follows
review. Next Goal verifies operational GitHub state and actual entry conditions.

| Ticket | Evidence PR / reviewed head / CI / handoff |
|---|---|
| #168 | [PR #173](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/173), reviewed implementation `b3adca9bd2be1bb4c55f1b4af8e0fb83ea344c3e`; [NORMAL GPT-6.1 Sol Medium APPROVE](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/173#issuecomment-5933277063), no findings. Collector/crafting/boundary 47, Application Crafting 33 and workflow 24 tests passed independently; [implementation-head CI 36874254541](https://github.com/ThibaultPoujat/Tyrian_Ledger/actions/runs/36874254541) has all five jobs green. [Typed source/actor/coverage handoff and endpoint fixtures](../../architecture/account-holdings-collector.md); four-read/30-actor cancellation evidence, captured credential, checked separate delivery and no coherence claim. API/domain-only, no UI preview. Later evidence-only head and [final-head checks](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/173/checks) are recorded in the PR; verify them before successor entry. |
| #169 | [PR #174](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/174), runtime review head `4f71aa25414a94fd9737e1483bac84019e643691`; NORMAL independent GPT-6.1 Sol High, exact final evidence-head approval and [final-head CI](https://github.com/ThibaultPoujat/Tyrian_Ledger/pull/174/checks) recorded in PR before delivery. Policy/Crafting Application 53, collector/crafting/boundary 65, workflow 24 and full backend 755 tests passed at runtime head; additional response-mapper/browser evidence and final checks are in PR. [Typed policy/collector handoff](../../architecture/account-holdings-collector.md) retains all-tab coverage, matching-copy ambiguity, invocation floor, real actor/access prerequisites and deferred admission. [Actual 1920×1080 legacy state renders, original reference comparison and reproduction/preview](../../ux/evidence/TKT-M22-P02B/README.md): mixed/inactive rejection and eligible single actor, keyboard/axe/125% zoom. Later changes only add tests/visual/batch evidence; verify exact reviewed/final head before successor entry. |
| #170 | Pending implementation. Guarded writer/publication table, restore/restart races, fresh GPT-6.1 Sol XHigh approval and final-head CI required. |
| #171 | Pending implementation. Shared projection, physical/source limitations, transfer replay, protection and SQLite integration evidence required. |

## Exit and escalation

After #171 merge, stop coding at #150. Inspect integrated account/protection/
epoch behavior, retain UI feedback status, prepare 3–5 next contracts and prioritize
the early Windows feasibility probe with actual target-environment evidence.
Do not close P02 or #150 merely because a collector/projection exists: P02-E action-relevant
reconciliation/correspondence proof, transfer/craft basis, lifecycle settlement and all package exits
still need evidence. #96/#97 remain after package acceptance.

Escalate sooner only for invalid dependencies, a genuine owner/architecture/
financial/security decision, or two failed attempts at the same blocker. Routine
fixes stay in the ticket. No automatic next-ticket execution or merge.
