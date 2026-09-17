# Codex Development Workflow

## Purpose

Codex implements one bounded ticket at a time. The owner supplies functional intent and makes durable product decisions. The application runtime remains deterministic and contains no application LLM.

## Context model

Each implementation session loads the minimum durable context:

1. `CURRENT.md`;
2. `AGENTS.md`;
3. `docs/context/permanent-context.md`;
4. current milestone context;
5. one assigned ticket;
6. relevant `docs/verification/VERIFY-REGISTER.md` entries;
7. `docs/workflow/model-effort-guide.md`;
8. specialized source/spec/ADR files only when required.

Do not load all historical milestones or the entire specification tree for a routine ticket.

For Signals/plan/crafting work, `docs/specs/signals.md` is an active product source of truth and should be read when the assigned ticket depends on its concepts.

## Generated live-state authority split

The generated live-state block in `CURRENT.md` combines multiple authorities; GitHub is not the authority for every field:

- **Operational delivery state:** GitHub merged PRs, issue open/closed state, and milestone assignment/title.
- **Execution order / next valid ticket:** issue #98 and `docs/milestones/INDEX.md`. Numeric issue ordering is not authoritative.
- **Review effort and gates:** `docs/workflow/model-effort-guide.md`.

Issue #98 and `docs/milestones/INDEX.md` must agree on execution order. If they conflict, treat that as a source-of-truth contradiction to repair rather than silently selecting one.

At session start:

1. inspect the assigned issue/PR and the relevant owning authority for each generated field;
2. compare those authoritative values with the generated live-state block in `CURRENT.md`;
3. repair stale generated fields from their owning authority before using the block as handoff state;
4. never infer execution order from GitHub issue numbers or review gates from risk class/issue metadata;
5. never rewrite durable `CURRENT.md` narrative merely to repair generated live state.

TKT-M21-S01 / #129 owns the deterministic post-merge automation for this block. Until it lands, the implementation/delivery agent performs the reconciliation manually. After it lands, per-field session-start reconciliation remains the fallback if automation failed or state is stale. The owner should not need to edit normal handoff state.

## Ticket versus session

A ticket is the unit of product work. Default rule:

**one implementation ticket = one implementation session.**

A separate session may be used for focused fixes/tests if necessary, but must remain scoped to the same ticket. Do not begin the next ticket merely because context remains.

## Standard implementation lifecycle

1. Read the ticket and minimum context.
2. Reconcile the `CURRENT.md` generated fields against their owning authorities.
3. Inspect current Git/repository state and relevant VERIFY items.
4. Make a short in-session plan of at most five steps. Use dedicated Plan mode only when `model-effort-guide.md` or a genuine unresolved owner decision warrants it.
5. If a genuine ambiguity/contradiction cannot be resolved from the repository, ask the owner with a recommendation and concise alternatives before implementing; do not ask routine technical questions.
6. Implement only the ticket outcome.
7. Run focused validation, then broader checks when justified.
8. Inspect the diff for scope expansion, secrets, data-loss risk, stale contradictory docs, repository-language violations, and accidental English user-facing UI copy where French is required.
9. Run the review path selected by `model-effort-guide.md`.
10. Commit/push/open or update the PR according to `delivery-protocol.md`.
11. Write the required completion report including the short functional summary, then stop.

The next session recovers from authoritative GitHub/repository state; it does not require previous chat history.

## Review paths

Review-model selection and effort are defined centrally in `docs/workflow/model-effort-guide.md`. **R3 by itself does not require Sol.**

### NORMAL

For tickets not listed in the explicit Sol gate:

- use the risk-based Terra effort from the model-effort guide rather than High for every ticket;
- use an independent Terra review subagent/check **inside the same implementation run** when supported;
- use Medium review by default for R0/R1 and High for R2/R3, escalating when findings or uncertainty justify it;
- run all ticket-required tests and CI;
- a second owner-triggered review session is optional, not a merge requirement;
- escalate to Sol only for unresolved high-consequence ambiguity, uncertain Important/Blocker findings, or explicit owner request.

### SOL-GATED

For the explicit active Sol-gated ticket list:

- implement/fix with Terra High by default;
- open the PR as Draft;
- complete required local validation and let required GitHub CI go green before spending the Sol review session;
- use `.codex/skills/tyrian-pr-review/SKILL.md` in a fresh separate Sol XHigh session;
- keep the PR Draft while findings remain;
- fix confirmed findings with Terra High, rerun affected validation/CI, and use a targeted fresh Sol re-review unless the fix materially broadened the authority/scope under review;
- after APPROVE and green validation, mark the PR Ready for Review;
- owner performs the final merge.

Draft state is the merge blocker. Do not rely on the owner remembering the Sol-gate list manually. If quota is exhausted, keep the Draft/handoff intact and resume later rather than weakening the gate.

## VERIFY and BLOCKED

`VERIFY` means an external fact is unresolved but safe work can continue without assuming it. `BLOCKED` means missing/contradictory information makes requested work unsafe or technically impossible.

Do not stop merely because a non-blocking external fact is uncertain. Record it and proceed with assumptions clearly isolated from financial truth.

## Anti-loop policy

- Maximum five planning steps.
- Prefer execution over repeated summaries.
- Do not reread unchanged files more than twice without new reason.
- Do not retry the same failed operation more than twice without changing the approach.
- Do not start a second NORMAL review session when a valid same-run independent review already completed.
- Do not spend Sol review quota before required validation/CI is green unless Sol is explicitly needed to resolve a blocking high-consequence ambiguity.
- Stop after the coherent ticket slice is delivered.

## Testing policy

For code tickets, test changed behavior and dangerous boundaries around it. Run narrow relevant tests first; broaden when integration risk justifies it.

R3 tickets still require edge/regression cases appropriate to their authority, regardless of whether review path is NORMAL or SOL-GATED. The quota-aware policy changes effort/reviewer allocation, not correctness standards.

Never weaken/delete a test merely to obtain green CI.

## Architecture and ADRs

Create/update an ADR only for a durable cross-cutting decision. Ordinary ticket implementation does not require a new ADR. Superseded ADRs remain historical records and are not active instructions.

## Repository and UI language rule

Repository-facing artifacts use English: documentation filenames/prose except quoted UI copy, code/identifiers, internal domain terms, enums/reason codes, migrations and tests.

User-facing UI/UX labels, actions, messages, errors, explanations, empty/degraded states, notifications and accessibility text are French. Business/financial logic must use structured semantics and must not depend on parsing translated presentation strings.

## Required functional summary

Every implementation ticket ends with a short plain-language summary (normally 2-6 sentences) answering:

- What can the user/project do now that it could not do before?
- What important behavior changed?
- What remains deliberately outside this ticket?

Do not substitute a file list or technical changelog for this summary.
