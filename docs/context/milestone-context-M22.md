# Milestone Context — M22

## Intended outcome

A quiet, local-first Windows second-screen profit assistant that respects the
player's session constraints, maintains correct resources/evidence, follows the
six approved prototypes, and can be installed by the owner and later friends.

## Current scope and readiness

P00 / #148, P01A / #149 and P01B / #154 are merged foundations. Accepted #156
review/CI is in [B1](../milestones/M22/batch-01.md). C02 / #157 prepares four
contracts: #158 → #159 → #160 → #161, then #150 checkpoint. INDEX and #98 own
order. Prepared successors need their entry conditions verified, not a new Astra
planning session after every merge. Keep one ticket/Goal/PR and owner merge.

The [remaining map](../milestones/remaining-delivery-map.md) decomposes all
P01–P07 work. Later units remain Planned; Astra promotes the next 3–5 at batch
checkpoints or resolves an earlier genuine contract/owner decision. #150 stays
open until P01–P06 evidence and owner progression acceptance; #96/#97 follow.
UI previews are reusable fixture-backed components, not live capability.

## Shared invariants

- Integer-copper deterministic financial truth; no gameplay/API writes.
- Combined resource demands and atomic starts cannot overcommit.
- Actual API facts supersede provisional reports only with adequate relevant evidence; stale/incomplete snapshots are not proof of contradiction.
- All-character inventory/capability and equipped/template protection coverage is explicit.
- Both session objectives, immutable commitments and honest profit/cash/time semantics.
- Quiet by default; native alerts need actionable urgency and durable deduplication.
- API requests remain endpoint-aware, cached, bounded and account-epoch scoped.
- French UI; latest six PNGs mandatory for visual work; 1920×1080 acceptance.
- Local credentials stay outside browser/SQLite/logs; host remains loopback-only.
- Recovery, replay, restart and private-data boundaries require meaningful tests.

## Exit

P01–P06 gates close with evidence, release hardening passes, owner usage confirms
worthwhile actions and usable screens, and outcomes remain attributed to evidence
without inventing profit or allowing autonomous rule changes. Owner merges/releases.
