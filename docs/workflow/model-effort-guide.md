# Model and Review Policy

Owner direction updated 2026-09-28: Astra prepares bounded contracts, Luna can
implement sufficiently specified tickets, and stronger review is reserved for
consequential changes. Model availability/usage accounting is controlled by the
host product. This policy promises neither a quota saving nor a quota bypass.

## Principles

One implementation ticket per Goal/session. Use the smallest relevant context,
not every historical document/image. Tests, invariants, CI and independent
review remain mandatory evidence. Do not start another ticket automatically.

Model names here describe roles available to the owner. If the named model or
independent review is unavailable, preserve a handoff/Draft; report that limit.
Never pretend an author self-review is independent or silently substitute for a
required stronger review. Explicit ticket choices below take precedence over
default implementation model selection.

## Risk and model defaults

| Risk | Implementation default | NORMAL independent review |
|---|---|---|
| R0: mechanical docs/maintenance | Luna Medium | Luna Medium |
| R1: bounded UI/product/workflow | Luna High | Luna High |
| R2: complex cross-layer/stateful | Sol Medium; Luna High only with a fully specified bounded ticket | Sol Medium |
| R3: money/resources, reconciliation, security or persistence authority | Sol High; Luna High only when explicitly authorized by a bounded contract | Sol High |

Astra is used for architecture/product decisions, ticket decomposition and
milestone evidence checks, or an explicitly requested difficult audit. It is
not required for routine implementation or every PR. Max is exceptional.
Risk alone does not imply a *separate Sol XHigh* gate; only the explicit list does.

## Planning and Goal boundaries

A short in-session plan (at most five steps) is sufficient for a Ready ticket.
Use dedicated planning for unresolved product/architecture/financial ambiguity
or owner request. Do not invent exact file-level instructions for a future
package before its dependencies are implemented.

See [Goal handoff](goal-session.md). Do not launch parallel worker swarms by
default. One independent reviewer is appropriate when the contract calls for it.
After two unsuccessful attempts at the same blocker, diagnose and preserve an
evidence-rich handoff; escalate instead of repeatedly retrying or broadening scope.

## NORMAL path

Implement, validate, inspect the diff and run one independent review using
`.codex/skills/tyrian-pr-review/SKILL.md` with the model/effort above. The reviewer
starts from the ticket and diff and checks evidence independently. Same-run
review is sufficient; a second owner-triggered session is not the default.
Keep a PR Draft when required validation, review or visual evidence is missing.
Mark Ready only after required checks and review pass. The owner merges.

TKT-M22-P00 / #148 is a one-time owner-requested Astra planning/documentation
bootstrap (R1), reviewed independently by Sol Medium for authority/workflow
consistency. It does not implement runtime financial changes.

## Separate Sol review gate

The following unmerged tickets require fresh independent **Sol XHigh** review:

- #149 / TKT-M22-P01A — duplicate resource aggregation and atomic start; Luna High implementation explicitly permitted for this bounded fix.
- #96 / TKT-M22-02 — final security/recovery/release hardening; Sol High implementation default.

Completed gates #133, #93 and #94 are historical, not active review requirements.
Add newly ready high-consequence tickets explicitly here when their contracts
require this gate; do not infer or silently remove gates from labels alone.

1. Open the implementation PR as **Draft**.
2. Finish required local validation and green CI before the fresh Sol XHigh pass.
3. Review with `.codex/skills/tyrian-pr-review/SKILL.md` in an independent context.
4. Keep Draft for Blocker/Important findings or missing required evidence.
5. Fix within the same ticket, rerun affected checks and request a targeted fresh
   Sol re-review of findings/changes. Full review repeats only if scope broadened.
6. Mark Ready after APPROVE and required validation remain green; stop for owner
   merge. Do not bypass this gate when quota runs out.

## Quota and handoff

Do not claim the entire repository can be safely delivered in one five-hour
allowance. Keep acceptance examples in tickets, use focused tests first, avoid
rereading history and save progress in Git. On interruption, record branch/head,
completed criteria, exact failures, uncommitted state and next action. A later
session resumes the same ticket. Quota affects scheduling, never truth or gates.

## Superseded annotations

This is the sole authority for model/effort, separate review and Draft blocking.
Old Terra defaults and blanket R3/XHigh or mandatory Plan-mode language in
historical tickets are superseded. Functional acceptance, invariants and required
validation are not weakened. UI work also obeys the visual evidence contract.
