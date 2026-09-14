# Codex Model and Reasoning Effort Guide

Model names and availability change faster than project architecture. This file
sets the active **quota-aware execution and review policy**. Risk class describes
the consequence of an error; it does **not** by itself require a separate Sol
session.

## Principles

1. Tests, CI, deterministic invariants, and review evidence are the primary safety system.
2. Match Terra effort to risk instead of using High for every ticket.
3. An independent Terra review subagent/check inside the implementation run is sufficient for the NORMAL review path when supported by the coding environment.
4. Spend a separate Sol session only where the expected cost of a subtle error justifies the owner's limited Plus quota.
5. Do not spend Sol review quota on a PR that still has failing required validation/CI unless Sol is explicitly needed to resolve the failure safely.
6. Escalate any NORMAL ticket to Sol only when Terra reports unresolved high-consequence ambiguity, important review findings remain uncertain, or the owner explicitly requests it.
7. Max effort is an exception, never a default.

## Risk classes and default Terra effort

Risk classes control test depth, reviewer focus, and default Terra effort:

- **R0 — mechanical/low-consequence maintenance:** Terra Medium by default.
- **R1 — normal product implementation:** Terra Medium by default; escalate to High when the work becomes materially cross-layer, stateful, or ambiguous.
- **R2 — complex cross-layer or stateful work:** Terra High by default.
- **R3 — financial, accounting, persistence/data-loss, security/private-data, statistical, recommendation, network-exposure, or architecture-authority work:** Terra High by default.

A lower default effort never weakens acceptance criteria, tests, CI, security, or
deterministic correctness requirements. If Medium encounters uncertainty or
non-trivial findings, escalate the same ticket to High rather than guessing.

R3 still requires stronger tests and more deliberate review, but **R3 does not automatically mean a separate Sol review**.

## Planning mode

Every implementation ticket still begins with a short in-session plan of no more
than five steps. Dedicated product **Plan mode is not required for every ticket**.
Use it when:

- the owner explicitly requests it;
- a genuine owner/product decision must be resolved before code changes;
- requirements or canonical documents materially contradict each other;
- architecture, destructive behavior, security, financial authority, or scope is
  ambiguous enough that building first would create avoidable rework.

Otherwise make the short plan in the implementation session and proceed. If a
genuine ambiguity or owner decision appears during planning or implementation,
pause and ask the owner with a recommended choice and concise alternatives. Do
not ask the owner to decide routine technical details already authorized by the
ticket/repository.

## NORMAL implementation and review path

Unless a ticket is in the explicit Sol review gate below:

- Plan/implementation/fixes: use the risk-based Terra effort above.
- Review: use an independent Terra review subagent/check **inside the same implementation run** when supported.
  - R0/R1 review may use Terra Medium by default and escalate to High for material findings/uncertainty.
  - R2/R3 review uses Terra High by default.
- Run all ticket-required tests and CI.
- A second owner-triggered review session is **not** required by default.
- Use a separate review session only for an explicit escalation or when the coding environment cannot provide an independent same-run review.
- Do not put legacy wording such as `R3 requires fresh flagship XHigh review` in the PR body.

## Separate Sol review gate

The following **unmerged roadmap tickets** require a separate fresh **Sol XHigh**
review before owner merge because they establish especially consequential
financial/recommendation authority or final whole-product safety:

- #88 / TKT-M19-04 — `What Should I Do?` recommendation orchestration;
- #90 / TKT-M20-02 — personal-performance ranking weight;
- #93 / TKT-M21-02 — crafting opportunity-cost economics;
- #94 / TKT-M21-03 — bounded crafting-path economics;
- #96 / TKT-M22-02 — final security/recovery/release hardening.

Completed historical Sol-gated tickets remain documented in their tickets/PRs;
they are intentionally omitted from this active gate list.

For an active SOL-GATED ticket:

1. Plan/implementation/fixes: **Terra High** by default.
2. Create the PR as **Draft**.
3. Complete required local validation and push the implementation.
4. Let required GitHub CI finish. If CI is red, fix with Terra High and revalidate before spending a Sol review session.
5. With required validation/CI green, run a fresh separate **Sol XHigh** review using `.codex/skills/tyrian-pr-review/SKILL.md`.
6. If changes are required, use Terra High for corrections, keep the PR Draft, rerun affected validation/CI, then perform a **targeted fresh Sol re-review** focused on the prior findings, changed diff, and regression risk.
7. Require a full Sol re-review only when the fix materially broadens the changed authority/scope beyond the prior findings.
8. Mark Ready only after Sol returns APPROVE and required validation is green.

The Draft state is the merge-safety mechanism; the owner should not need to
remember the list manually.

## Quota exhaustion

If the owner's included agentic allowance is exhausted:

- do not weaken acceptance criteria or tests;
- do not substitute a cheaper reviewer for an explicit SOL-GATED review;
- leave an unfinished SOL-GATED PR Draft and preserve a clear repository handoff;
- resume implementation/review after quota returns or after the owner deliberately chooses another supported allowance/credit option.

Quota pressure changes scheduling and effort selection, not correctness gates.

## Superseding legacy ticket annotations

As of 2026-09-14, this file is authoritative for **model effort, Plan-mode use,
review-model selection, and PR Draft blocking**. Older ticket/workflow text that
says every ticket uses Terra High, actual Plan mode is mandatory, an R3 ticket
automatically requires `fresh flagship XHigh`, or a NORMAL ticket requires a
separate fresh review session is superseded.

Ticket acceptance criteria, functional scope, validation requirements, and risk
classification remain authoritative and are not weakened by this review-policy
change.

## Max effort

Use Max only when XHigh leaves a real unresolved correctness ambiguity, a subtle
bug survives the normal implementation/review process, the task is unusually
cross-domain, or the owner explicitly requests the strongest possible audit.
