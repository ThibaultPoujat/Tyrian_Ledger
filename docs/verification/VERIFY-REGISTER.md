# VERIFY Register

Central register of unresolved or time-sensitive verification items for
Tyrian Ledger.

## Purpose

This file tracks facts, assumptions, external-contract questions, and other
items that must be verified before they can be treated as project facts.

It is a project-level index only.

The ticket, ADR, specification, or other authoritative project document where
the item originated remains the primary source of context and evidence.

## Rules

1. Never invent or silently resolve a VERIFY item.
2. A VERIFY item may only be marked RESOLVED when supporting evidence has been
   recorded in the relevant ticket, ADR, specification, or other authoritative
   project document.
3. When resolving an item, record:
   - what was verified,
   - the evidence/source,
   - the date,
   - the ticket that performed the verification.
4. If verification changes an architectural or durable cross-cutting decision,
   follow the project's ADR policy.
5. Do not delete resolved items. Mark them RESOLVED so the project retains an
   audit trail.
6. New VERIFY items discovered during a ticket must be added to this register
   before the ticket is considered complete.
7. A ticket must not claim completion while introducing an unresolved VERIFY
   item that belongs to the ticket's acceptance criteria.
8. VERIFY items concerning external APIs, quotas, permissions, legal
   constraints, security requirements, or other time-sensitive facts should
   include an authoritative source where available.
9. When an existing VERIFY item is affected by new evidence, update its status
   and evidence rather than creating a duplicate entry.
10. The register must remain concise. Detailed investigation belongs in the
    relevant ticket or project document.

## Status values

- OPEN — verification is still required.
- IN PROGRESS — verification is actively being investigated.
- RESOLVED — sufficient evidence has been recorded.
- REJECTED — the assumption/question was determined not to apply.
- SUPERSEDED — replaced by a newer verification or decision; link the
  replacement.

## Register

| ID | Status | Description | Source / Evidence | Owner Ticket | Date Added | Date Resolved |
|---|---|---|---|---|---|---|
| VERIFY-001 | OPEN | Upstream license terms of the `Qwen/Qwen3.8-27B` base model for the selected MTPLX artifact (`Youssofal/Qwen3.8-27B-MTPLX-Optimized-Speed-FP16`); the README states apache-2.0 on the MTPLX repo, but the base model license was not independently checked. | `docs/tickets/TKT-M0-01.md` — VERIFY items | TKT-M0-01 | 2026-08-21 | — |
| VERIFY-002 | OPEN | Whether the selected FP16 MTPLX artifact is the correct performance choice for this developer Mac's chip generation; `mtplx hardware` timed out during inspection and was not completed (README: M1/M2 use the FP16 sibling; M3+ use the parent artifact). | `docs/tickets/TKT-M0-01.md` — VERIFY items | TKT-M0-01 | 2026-08-21 | — |
| VERIFY-003 | OPEN | A live MTPLX generation smoke test (`mtplx runtime-smoke` or one `mtplx ask`) before relying on the selected model for sustained development; only inspect verdicts were recorded so far. | `docs/tickets/TKT-M0-01.md` — VERIFY items | TKT-M0-01 | 2026-08-21 | — |

## Verification history

Resolved items remain in the table above.

For significant verification decisions, detailed evidence should remain in the
ticket or other authoritative project document rather than being duplicated
here.
