# Milestone Context — M22

## Intended outcome

A quiet, local-first Windows second-screen profit assistant that respects the
player's session constraints, maintains correct resources/evidence, follows the
six approved prototypes, and can be installed by the owner and later friends.

## Current scope and readiness

The merged loop/calculation work (#95/#145) is a foundation, not proof that the
audit findings are resolved. P00 / #148 prepares authority and references.
P01A / #149 is the first bounded code ticket after P00 merges. G01 / #150 tracks
remaining P01–P06 exits; it is not a coding Goal. #96 remains final hardening,
then #97 evaluates supported outcomes. INDEX and #98 own exact order.

[Approved delivery plan](../milestones/approved-delivery-plan.md) maps all 19
findings and package exits. Only Ready child tickets may be implemented. Each
package needs decomposition at its checkpoint; do not implement a package in
one session. Preserve existing work and make corrective tickets explicit.

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
