# Milestone Context - M22: Continuous Operation, Hardening, and Evaluation

## User outcome

The local application continuously turns permitted fresh evidence into materially new actionable Signals, is dependable to operate and recover, and can evaluate observed plan outcomes without trusting recommendation assumptions indefinitely.

## Invariants

- Notifications are a delivery surface for deterministic Signals; they never execute trades.
- Unchanged/no-action conditions are de-duplicated or silent rather than repeatedly interrupting the user.
- Source refresh/freshness is endpoint-aware and conservative; unresolved external rate/cache contracts stay in VERIFY.
- Calculation explanations derive from the same canonical, versioned backend decisions and verified account-scoped evidence as actionable Signals/Plans, never a second UI calculator; unknown remains unknown.
- Local execution shadow reconciles with verified evidence without double counting or silently guessing contradictions.
- Local host/secret boundaries receive a fresh release audit.
- Backup/restore is exercised on representative populated data.
- Outcome evaluation distinguishes started/executed plans from ignored/unexecuted suggestions and preserves rule/configuration versions.
- Realized strategy attribution is additive and non-overlapping: Trading/Flipping, Crafting, Unclassified. Open/unrealized result stays separate.
- User-facing release copy on the primary journeys is French.

## Sequence

TKT-M22-01 / #95 follows the M21 `Mes Signaux` plan/crafting foundation and implements the continuous decision loop plus actionable local notification delivery.

TKT-M22-S01 / #145 follows #95 and adds French `Réglages > Comprendre mes calculs` and contextual `Pourquoi ?` with versioned theoretical math, the account's actual inputs and provenance, intermediate results, bound constraints and truthful unknown or degraded cases.

TKT-M22-02 / #96 then hardens the whole 0.1 shape around the primary `Mes Signaux / Artisanat / Réglages` journeys, plan/shadow reconciliation, restart persistence, accessibility, recovery and packaging.

TKT-M22-02 / #96 also validates the privacy, source provenance, accessibility and backend/UI parity of the calculation-explanation surfaces.

TKT-M22-03 / #97 evaluates observed Signal-plan outcomes only after that hardening gate.

## Exit

Critical 0.1 E2E journeys pass, clean-machine start/recovery is documented, and the project can identify weak strategies/components from observed evidence without autonomously changing financial rules or fabricating outcomes for actions the owner never executed.
