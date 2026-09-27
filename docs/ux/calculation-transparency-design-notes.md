# Calculation Transparency — Design Acceptance Notes

Status: implementation planning for TKT-M22-S01 / #145; the source of financial truth remains the canonical, versioned C# policy and the typed decision snapshot. These are **not** implementation-complete notes.

## The product question

The user must be able to answer, without reading code: **What did Tyrian Ledger calculate, why did it choose that rule, which actual data from my account did it use, what is unknown, and what would make the action eligible?**

## Surface and hierarchy

- Under existing `Réglages`, add the compact `Comprendre mes calculs` disclosure. This is not a new top-level destination or a raw diagnostics table.
- Categories: `Capital et limites`, `Prix, frais et profit`, `Artisanat`, `Signaux et Plans`, `Fiabilité des données`.
- Each category distinctly separates `Comment ça marche` (canonical formula and version) from `Avec mes données` (real, account-scoped, timestamped values and exact arithmetic).
- Existing `Pourquoi ?` on a Signal or Plan should expose the matching decision trace for that specific decision and link back to the relevant rule in Settings.
- Preserve the approved owner-provided compact dark second-screen direction. Use progressive disclosure, not an exhaustive initial dashboard.

## Required example states

| Scenario | User must see | Safety |
|---|---|---|
| Valid wallet, exposures and sizing | Amounts, denominator, configured percentages, rounding, remaining cash and each binding cap | No second frontend calculator |
| Missing or invalid sizing snapshot | Specific absent/invalid wallet or exposure evidence and the safety consequence | No fabricated bankroll |
| Owned sale with unknown acquisition basis | Known ownership/quantity, possible proceeds and precise inability to claim historical profit | Distinguish sell/list rules from buy caps |
| Plan conflicts with a current action | Same snapshot and candidate ID as its originating Signal, competing reservation and selection reason | No stale executable instruction |
| Craft cannot price an ingredient | Exact ingredient/evidence gap, attempted supported procurement paths and reason for deferral | Unknown never equals zero |
| Refresh or account switch | Per-source age, stale flag and account scope reset | No cross-account explanations |
| Fee calculation | Two separately rounded canonical components and model limitations (VERIFY-013) | Integer-copper authority |

## Review evidence

Capture real-app browser screenshots of populated, rejected, empty and degraded explanations at 1920×1080 and narrow width, with sensitive values appropriately redacted before sharing. Independently compare the displayed intermediate math to typed backend results for the same decision ID/policy version. The owner must approve the interaction fit before #96 treats it as the release UX.

Do not claim any of these acceptance outcomes from this planning-only file. Full tests, backend/UI implementation and review belong to PR #146 after #95 merges.
