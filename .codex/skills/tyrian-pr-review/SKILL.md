---
name: tyrian-pr-review
description: Review a Tyrian Ledger pull request or ticket branch independently for correctness, acceptance-criteria coverage, financial/data/security invariants, tests, and scope. Use as the separate review skill for SOL-GATED tickets and explicit escalations; NORMAL tickets may use the same checklist through an independent Terra review subagent/check.
metadata:
  short-description: Independent Tyrian Ledger PR review
---

# Tyrian Ledger PR Review

## Purpose

Perform a findings-first independent review of one Tyrian Ledger ticket PR. The
same review discipline applies whether invoked by a NORMAL independent Terra
subagent/check or by a fresh separate SOL-GATED review session.

Default behavior is **review only**. Do not silently modify the branch. If the
owner explicitly asks for review-and-fix, report findings first, then make only
scoped corrections for the same ticket.

## Required inputs

Resolve:

- ticket ID / PR;
- PR base and head;
- current milestone;
- review path from `docs/workflow/model-effort-guide.md`;
- implementation validation evidence if present.

If a PR/ticket is identifiable from context, do not ask the owner to repeat it.

## Read order

1. `CURRENT.md`
2. `AGENTS.md`
3. `docs/context/permanent-context.md`
4. current milestone context
5. assigned ticket
6. `docs/workflow/model-effort-guide.md`
7. relevant VERIFY entries
8. relevant canonical specs/ADRs
9. PR diff and touched source/tests

Do not load unrelated historical tickets.

## Review procedure

### 1. Establish the contract

Extract the ticket goal, acceptance criteria, non-goals, dependencies, risk
class, review path, required tests, owner decisions, and functional outcome.

### 2. Inspect the diff before accepting the author's explanation

Check:

- scope matches one ticket;
- no unrelated cleanup/feature creep;
- obsolete architecture is not accidentally reintroduced;
- docs and code agree;
- deleted tests/guards are justified;
- secrets/private account data are absent from source/logs/fixtures/payloads.

### 3. Review by risk domain

Always apply relevant sections of `.codex/skills/tyrian-pr-review/references/checklist.md`.

For financial/accounting work, recompute representative examples independently
and inspect rounding/partial/unknown/overflow behavior.

For persistence/sync, look for idempotency, transaction boundaries, uniqueness,
partial-failure data loss, migration downgrade/upgrade assumptions, and rebuild
semantics.

For statistics/scoring, test insufficient samples, irregular observations,
normalization dominance, deterministic ordering, and explanation consistency.

For recommendation/risk logic, verify max-bid/cash-reserve/exposure constraints
cannot be bypassed by composition.

For security, inspect actual data flow from secret store -> gateway ->
application -> browser/logs, not only redaction helper names.

### 4. Validate tests and evidence

Run or inspect the narrow required tests first. Broaden when integration risk
justifies it. A test that duplicates the implementation formula without an
independent expected vector is weak evidence for high-risk financial logic.

Never weaken tests to make the PR pass.

### 5. Map every acceptance criterion

For each criterion mark:

- `PASS` with evidence;
- `FAIL` with finding;
- `BLOCKED` with the missing evidence/decision;
- `NOT TESTABLE YET` only when the ticket explicitly allows it.

### 6. Produce the review report

Report findings first, ordered:

- **Blocker** — merge would risk financial/data/security correctness, violate a
  hard boundary, or fail the ticket's core outcome.
- **Important** — material bug, missing acceptance behavior/test, migration or
  reliability issue that should be fixed before merge.
- **Minor** — bounded maintainability/docs/test clarity issue that does not make
  the feature wrong.

Every finding includes path/location, evidence, functional impact, and concise
recommended correction.

Then include:

1. **Functional summary** — 2-6 sentences describing what the PR functionally
   changes for the user/project.
2. **Acceptance-criteria matrix**.
3. **Validation reviewed/run**.
4. **VERIFY/uncertainty changes**.
5. **Residual risk**.
6. **Verdict** — `APPROVE`, `CHANGES REQUESTED`, or `BLOCKED`.

If there are no findings, say so explicitly; do not invent stylistic nits.

## Independence and model effort

Use `docs/workflow/model-effort-guide.md` as authority.

- **NORMAL:** an independent Terra review subagent/check is sufficient by default
  together with ticket-required tests and CI. A separate session is optional.
- **SOL-GATED:** review in a fresh separate Sol XHigh session. The PR must remain
  Draft until this review returns APPROVE and required validation is green.
- Risk class R3 alone does not select Sol.

For a SOL-GATED re-review after fixes, focus on the prior findings and regression
risk while still checking new changes for introduced defects.

## PR state for SOL-GATED reviews

If a SOL-GATED PR is not Draft when review begins, treat that as an **Important
process finding** and convert it to Draft when the available GitHub tooling and
review role permit. Do not mark it Ready while Blocker/Important findings remain.
After APPROVE and green required validation, it may be marked Ready for the
owner's final merge decision.

## Stop condition

After the review report/state handoff, stop. Do not start the next ticket. Do not
merge.
