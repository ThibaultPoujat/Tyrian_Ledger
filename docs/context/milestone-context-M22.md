# Milestone Context - M22: Convenience, Hardening, and Evaluation

## User outcome

The local application is easier to operate daily, recover from failure, and
critically evaluate rather than trusting recommendation assumptions forever.

## Invariants

Alerts never execute trades and are de-duplicated. Local host/secret boundaries
receive a fresh audit. Backup/restore is exercised on populated data.
Recommendation evaluation distinguishes observed user-executed outcomes from
unexecuted suggestions and preserves rule/configuration versions.

Whole-product hardening follows the complete dashboard, scanner,
recommendation, personal-learning, investment, crafting, and alert feature set.
Outcome evaluation begins only after that hardening gate.

## Exit

Critical E2E journeys pass, clean-machine start/recovery is documented, and the
project can identify weak strategies/components from observed evidence without
autonomously changing financial rules.
