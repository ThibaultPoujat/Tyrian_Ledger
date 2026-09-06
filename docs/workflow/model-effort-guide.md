# Codex Model and Reasoning Effort Guide

Model names and availability change faster than project architecture. This file
sets the active **quota-aware execution and review policy**. Risk class describes
the consequence of an error; it does **not** by itself require a separate Sol
session.

## Principles

1. Tests, CI, deterministic invariants, and review evidence are the primary safety system.
2. Use GPT-5.6 Terra High for normal planning, implementation, fixes, and review work.
3. An independent Terra review subagent inside the implementation run is sufficient for the normal review path when supported by the coding environment.
4. Spend a separate Sol session only where the expected cost of a subtle error justifies the owner's limited Plus quota.
5. Escalate any normal ticket to Sol when Terra reports unresolved high-consequence ambiguity, important review findings remain uncertain, or the owner explicitly requests it.
6. Max effort is an exception, never a default.

## Risk classes

Risk classes remain useful for deciding test depth and reviewer focus:

- **R0** — mechanical/low-consequence maintenance.
- **R1** — normal product implementation.
- **R2** — complex cross-layer or stateful work.
- **R3** — financial, accounting, persistence/data-loss, security/private-data, statistical, recommendation, network-exposure, or architecture-authority work.

R3 still requires stronger tests and more deliberate review, but **R3 does not automatically mean a separate Sol review**.

## Default implementation path

Unless a ticket is in the explicit Sol review gate below:

- Plan/implementation/fixes: **Terra High** by default.
- Review: independent **Terra High review subagent** plus the ticket's required tests and CI.
- A separate fresh review session is optional rather than mandatory.
- Do not put legacy wording such as `R3 requires fresh flagship XHigh review` in the PR body.

## Separate Sol review gate

The following remaining roadmap tickets require a separate fresh **Sol XHigh** review before owner merge because they establish especially consequential financial/recommendation authority or final whole-product safety:

- #75 / TKT-M15-01 — canonical GW2 fee policy;
- #76 / TKT-M15-02 — FIFO lot matching;
- #77 / TKT-M15-03 — realized/unrealized P&L;
- #85 / TKT-M19-01 — historical analytics;
- #86 / TKT-M19-02 — opportunity score/anomaly logic;
- #87 / TKT-M19-03 — bankroll/position sizing;
- #88 / TKT-M19-04 — `What Should I Do?` recommendation orchestration;
- #90 / TKT-M20-02 — personal-performance ranking weight;
- #93 / TKT-M21-02 — crafting opportunity-cost economics;
- #94 / TKT-M21-03 — bounded crafting-path economics;
- #96 / TKT-M22-02 — final security/recovery/release hardening.

For these tickets:

- Plan/implementation/fixes: **Terra High** by default to conserve quota.
- PR state: **Draft** until the separate Sol review returns APPROVE.
- Review: fresh **Sol XHigh** session using `.codex/skills/tyrian-pr-review/SKILL.md`.
- If fixes are required, use Terra High for corrections; keep the PR Draft and perform a targeted fresh Sol re-review before marking Ready.
- The reviewer may mark the PR Ready only after APPROVE and all required validation is green.

The Draft state is the merge-safety mechanism; the owner should not need to remember the list manually.

## Superseding legacy ticket annotations

As of 2026-09-06, this file is authoritative for **model/review selection and PR blocking**. Older ticket text that says an R3 ticket automatically requires `fresh flagship XHigh`, `Sol High implementation`, or a separate fresh review session is superseded unless the ticket appears in the explicit Sol gate above.

Ticket acceptance criteria, functional scope, validation requirements, and risk classification remain authoritative and are not weakened by this review-policy change.

## Max effort

Use Max only when XHigh leaves a real unresolved correctness ambiguity, a subtle bug survives the normal implementation/review process, the task is unusually cross-domain, or the owner explicitly requests the strongest possible audit.
