# Codex Model and Reasoning Effort Guide

Model names and availability change faster than project architecture. This file
sets the active **quota-aware execution and review policy**. Risk class describes
the consequence of an error; it does **not** by itself require a separate Sol
session.

## Principles

1. Tests, CI, deterministic invariants and review evidence are the primary safety
   system.
2. Match Terra effort to risk instead of using High for every ticket.
3. An independent Terra review subagent/check inside the implementation run is
   sufficient for the NORMAL review path when supported.
4. Spend a separate Sol session only where subtle failure would materially
   affect financial/recommendation/state authority or final release safety.
5. Do not spend Sol review quota on a PR that still has failing required
   validation/CI unless Sol is explicitly needed to resolve the failure safely.
6. Escalate any NORMAL ticket to Sol only when Terra reports unresolved
   high-consequence ambiguity, important findings remain uncertain, or the owner
   explicitly requests it.
7. Max effort is an exception, never a default.

## Risk classes and default Terra effort

- **R0 — mechanical/low-consequence maintenance:** Terra Medium by default.
- **R1 — normal product implementation:** Terra Medium by default; escalate to
  High when materially cross-layer, stateful or ambiguous.
- **R2 — complex cross-layer/stateful work:** Terra High by default.
- **R3 — financial, accounting, persistence/data-loss, security/private-data,
  statistical, recommendation, reconciliation/state-authority, network-exposure
  or architecture-authority work:** Terra High by default.

A lower default effort never weakens acceptance criteria, tests, CI, security or
deterministic correctness. If Medium encounters material uncertainty/findings,
escalate the same ticket to High rather than guessing.

R3 still requires stronger tests/reviewer focus, but **R3 does not automatically
mean a separate Sol review**.

## Planning mode

Every implementation ticket begins with a short in-session plan of no more than
five steps. Dedicated product Plan mode is not required for every ticket.

Use it when:

- the owner explicitly requests it;
- a genuine owner/product decision must be resolved before code changes;
- requirements/canonical documents materially contradict each other;
- architecture, destructive behavior, security, financial authority or scope is
  ambiguous enough that building first would create avoidable rework.

Otherwise make the short plan in-session and proceed. If genuine ambiguity or an
owner decision appears during implementation, pause and ask with a recommended
choice and concise alternatives. Do not ask the owner to decide routine
technical details already authorized by the ticket/repository.

## NORMAL implementation and review path

Unless a ticket is in the explicit Sol review gate below:

- plan/implement/fix with the risk-based Terra effort above;
- run an independent Terra review subagent/check **inside the same implementation
  run** when supported;
- R0/R1 review may use Terra Medium by default and escalate to High for material
  findings/uncertainty;
- R2/R3 review uses Terra High by default;
- run all ticket-required tests and CI;
- a second owner-triggered review session is not required by default;
- separate review sessions are for explicit escalation or environments that
  cannot provide independent same-run review;
- do not put obsolete `R3 requires fresh flagship XHigh` wording in NORMAL PRs.

## Separate Sol review gate

The following **currently unmerged roadmap tickets** require a separate fresh
**Sol XHigh** review before owner merge:

- #133 / TKT-M21-S05 — Signal plan/resource orchestration, local execution shadow
  and verified-state reconciliation;
- #93 / TKT-M21-02 — crafting opportunity-cost economics;
- #94 / TKT-M21-03 — bounded crafting-path economics and guided plan generation;
- #96 / TKT-M22-02 — final security/recovery/release hardening.

Previously completed Sol-gated tickets remain documented in their historical
issues/PRs and are intentionally omitted from this active list.

For an active SOL-GATED ticket:

1. Plan/implementation/fixes: **Terra High** by default.
2. Create the PR as **Draft**.
3. Complete required local validation and push the implementation.
4. Let required GitHub CI finish. If CI is red, fix with Terra High and
   revalidate before spending a Sol review session.
5. With required validation/CI green, run a fresh separate **Sol XHigh** review
   using `.codex/skills/tyrian-pr-review/SKILL.md`.
6. If changes are required, use Terra High for corrections, keep the PR Draft,
   rerun affected validation/CI, then perform a **targeted fresh Sol re-review**
   focused on prior findings, changed diff and regression risk.
7. Require a full Sol re-review only when the fix materially broadens changed
   authority/scope beyond prior findings.
8. Mark Ready only after Sol returns APPROVE and required validation is green.

Draft state is the merge-safety mechanism; the owner should not need to remember
the list manually.

## Current roadmap effort notes

These tickets are currently expected to use the NORMAL path unless escalated:

- #128 product/docs/roadmap consolidation — R1;
- #129 self-healing `CURRENT.md` workflow/state maintenance — R2;
- #130 `Mes Signaux` UX design spike — R1;
- #131 `Mes Signaux` MVP/new navigation — R2;
- #132 post-MVP UI/docs/code cleanup — R2;
- #95 continuous decision loop/actionable notifications — R2;
- #145 / TKT-M22-S01 calculation transparency and account-scoped decision provenance — R3 NORMAL, Terra High independent review; escalate only for unresolved high-consequence financial/state authority ambiguity;
- #97 Signal-plan outcome evaluation — R3 NORMAL with Terra High independent
  review unless a material ambiguity triggers escalation.

Risk/effort may be raised during implementation if actual scope exceeds the
ticket's expected boundary. Do not lower acceptance criteria because a ticket is
NORMAL.

## Quota exhaustion

If the owner's included agentic allowance is exhausted:

- do not weaken acceptance criteria or tests;
- do not substitute a cheaper reviewer for an explicit SOL-GATED review;
- leave an unfinished SOL-GATED PR Draft and preserve a clear repository
  handoff;
- resume implementation/review later rather than bypassing the gate.

Quota pressure changes scheduling/effort allocation, not correctness gates.

## Superseding legacy ticket annotations

This file is authoritative for **model effort, dedicated Plan-mode use,
review-model selection and PR Draft blocking**. Older ticket/workflow text that
says every ticket uses Terra High, actual Plan mode is mandatory, an R3 ticket
automatically requires fresh XHigh, or a NORMAL ticket requires a separate fresh
review session is superseded.

Ticket acceptance criteria, functional scope, validation requirements and risk
classification remain authoritative and are not weakened by review-policy
changes.

## Max effort

Use Max only when XHigh leaves a real unresolved correctness ambiguity, a subtle
bug survives the normal implementation/review process, the task is unusually
cross-domain, or the owner explicitly requests the strongest available audit.
